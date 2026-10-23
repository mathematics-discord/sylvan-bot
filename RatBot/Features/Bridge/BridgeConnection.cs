namespace RatBot.Features.Bridge;

using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Discord.Net;

/// <summary>Owns transport tasks, queues, and action authority for one connection.</summary>
public sealed class BridgeConnection
{
    private const int OutboundCapacity = 64;
    private const int ActionCapacity = 16;
    private const int OriginCapacity = 256;
    private const int RequestCapacity = 1024;

    private readonly ILogger _logger;

    private readonly Channel<Outbound> _outbound = Channel.CreateBounded<Outbound>(
        new BoundedChannelOptions(OutboundCapacity) { SingleReader = true, AllowSynchronousContinuations = false });

    private readonly Channel<BridgeAction> _actions = Channel.CreateBounded<BridgeAction>(
        new BoundedChannelOptions(ActionCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

    private readonly SemaphoreSlim _delivery = new(1, 1);
    private readonly Dictionary<ulong, BridgeOrigin> _origins = [];
    private readonly Queue<ulong> _originOrder = [];
    private readonly HashSet<string> _requests = new(StringComparer.Ordinal);
    private readonly DiscordSocketClient _discordClient;
    private readonly BridgeChannels _channels;

    private int _accepting = 1;

    public BridgeConnection(DiscordSocketClient discordClient, BridgeChannels channels, ILogger logger)
    {
        _discordClient = discordClient;
        _channels = channels;
        _logger = logger.ForContext<BridgeConnection>();
    }

    public void TryForward(BridgeMessage message, BridgeOrigin origin)
    {
        if (Volatile.Read(ref _accepting) == 0)
            return;

        var frame = new
        {
            version = BridgeProtocol.Version,
            type = "message",
            message,
        };

        if (!_outbound.Writer.TryWrite(new Outbound(frame, origin)))
        {
            _logger.Warning(
                "Bridge queue full; dropping message {MessageId} in guild {GuildId}, channel {ChannelId}",
                origin.MessageId,
                origin.GuildId,
                origin.ChannelId);
        }
    }

    public async Task RunAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task[] tasks = [];
        _logger.Information("Bridge client connected");

        try
        {
            await SendAsync(
                socket,
                new
                {
                    version = BridgeProtocol.Version,
                    type = "connected",
                    limits = new
                    {
                        outboundQueue = OutboundCapacity,
                        actionQueue = ActionCapacity,
                        origins = OriginCapacity,
                        requests = RequestCapacity,
                        frameBytes = BridgeProtocol.MaxFrameBytes,
                        replyCharacters = BridgeProtocol.MaxReplyCharacters,
                    },
                },
                stopping.Token);

            tasks =
            [
                DeliverAsync(socket, stopping.Token), ReceiveAsync(socket, stopping.Token),
                DispatchAsync(stopping.Token),
            ];

            var completed = await Task.WhenAny(tasks);
            await completed;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The connection or application is stopping.
        }
        catch (Exception exception)
        {
            _logger.Warning("Bridge transport stopped with {ErrorType}", exception.GetType().Name);
        }
        finally
        {
            Volatile.Write(ref _accepting, 0);
            await stopping.CancelAsync();
            _outbound.Writer.TryComplete();
            _actions.Writer.TryComplete();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception exception)
            {
                _logger.Debug("Bridge connection tasks stopped with {ErrorType}", exception.GetType().Name);
            }

            if (socket.State == WebSocketState.CloseReceived)
            {
                using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));

                try
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Bridge disconnected",
                        closing.Token);
                }
                catch (Exception)
                {
                    socket.Abort();
                }
            }
            else
            {
                socket.Abort();
            }

            while (_outbound.Reader.TryRead(out _))
            {
            }

            while (_actions.Reader.TryRead(out _))
            {
            }

            _origins.Clear();
            _originOrder.Clear();
            _requests.Clear();
            _delivery.Dispose();
            _logger.Information("Bridge client disconnected; connection work discarded");
        }
    }

    private async Task DeliverAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        await foreach (var item in _outbound.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Origin is { } candidate && !_channels.Contains(candidate.GuildId, candidate.ChannelId))
                continue;

            // An action arriving as SendAsync completes must see its delivered origin.
            await _delivery.WaitAsync(cancellationToken);

            try
            {
                await SendAsync(socket, item.Frame, cancellationToken);

                if (item.Origin is { } origin && _origins.TryAdd(origin.MessageId, origin))
                {
                    _originOrder.Enqueue(origin.MessageId);

                    if (_originOrder.Count > OriginCapacity)
                        _origins.Remove(_originOrder.Dequeue());
                }
            }
            finally
            {
                _delivery.Release();
            }
        }
    }

    private async Task ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[BridgeProtocol.MaxFrameBytes + 1];

        while (!cancellationToken.IsCancellationRequested)
        {
            var count = 0;
            ValueWebSocketReceiveResult received;

            do
            {
                received = await socket.ReceiveAsync(buffer.AsMemory(count), cancellationToken);

                if (received.MessageType == WebSocketMessageType.Close)
                    return;

                count += received.Count;

                if (received.MessageType == WebSocketMessageType.Text && count <= BridgeProtocol.MaxFrameBytes)
                    continue;

                _logger.Warning(
                    "Bridge rejected {FrameType} frame with {FrameBytes} bytes",
                    received.MessageType,
                    count);

                throw new WebSocketException("Bridge requires text frames of at most 65536 bytes.");
            } while (!received.EndOfMessage);

            var parsed = BridgeProtocol.Parse(buffer.AsMemory(0, count));

            if (parsed.Action is not { } action)
            {
                EnqueueError(parsed.RequestId, "invalid_frame", parsed.Error!);

                continue;
            }

            if (_requests.Contains(action.RequestId))
            {
                EnqueueError(action.RequestId, "duplicate_request", "requestId was already used in this connection.");

                continue;
            }

            if (_requests.Count == RequestCapacity)
            {
                _logger.Warning("Bridge request budget exhausted; disconnecting client");

                throw new WebSocketException("Bridge request limit reached; reconnect.");
            }

            _requests.Add(action.RequestId);

            if (!_actions.Writer.TryWrite(action))
                EnqueueError(action.RequestId, "busy", "Bridge action queue is full; action was not dispatched.");
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var action in _actions.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BridgeOrigin? origin;
            await _delivery.WaitAsync(cancellationToken);

            try
            {
                _origins.TryGetValue(action.MessageId, out origin);
            }
            finally
            {
                _delivery.Release();
            }

            if (origin is null)
            {
                EnqueueError(
                    action.RequestId,
                    "unknown_origin",
                    "Message was not delivered or its origin has expired.");

                continue;
            }

            await DispatchActionAsync(action, origin, cancellationToken);
        }
    }

    private async Task DispatchActionAsync(
        BridgeAction action,
        BridgeOrigin origin,
        CancellationToken cancellationToken)
    {
        List<string> sentIds = new();
        var dispatching = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        var requestOptions = new RequestOptions
        {
            CancelToken = timeout.Token,
            Timeout = 15000,
            RetryMode = RetryMode.RetryRatelimit,
        };

        try
        {
            if (!_channels.Contains(origin.GuildId, origin.ChannelId))
            {
                EnqueueError(action.RequestId, "channel_removed", "Bridge is no longer enabled in this channel.");

                return;
            }

            var destination = await ((IDiscordClient)_discordClient).GetChannelAsync(
                origin.ChannelId,
                options: requestOptions);

            var validDestination = destination is IGuildChannel guildChannel
                                   && guildChannel.GuildId == origin.GuildId
                                   && BridgeChannels.IsSupported(destination);

            if (!validDestination || destination is not IMessageChannel messageChannel)
            {
                EnqueueError(action.RequestId, "channel_unavailable", "The originating channel is unavailable.");

                return;
            }

            var isReply = string.Equals(action.Type, "reply", StringComparison.Ordinal);
            ImmutableArray<string> parts = isReply ? BridgeText.Split(action.Text!) : [string.Empty];

            foreach (var part in parts)
            {
                timeout.Token.ThrowIfCancellationRequested();

                if (!_channels.Contains(origin.GuildId, origin.ChannelId))
                {
                    EnqueueError(
                        action.RequestId,
                        "channel_removed",
                        "Bridge is no longer enabled in this channel.",
                        sentIds);

                    return;
                }

                dispatching = true;

                if (!isReply)
                {
                    await messageChannel.TriggerTypingAsync(requestOptions);
                }
                else
                {
                    var sent = await messageChannel.SendMessageAsync(
                        part,
                        options: requestOptions,
                        allowedMentions: new AllowedMentions(AllowedMentionTypes.None) { MentionRepliedUser = false },
                        messageReference: new MessageReference(
                            origin.MessageId,
                            origin.ChannelId,
                            origin.GuildId,
                            failIfNotExists: true));

                    sentIds.Add(sent.Id.ToString(CultureInfo.InvariantCulture));
                }

                dispatching = false;
            }

            Enqueue(
                new
                {
                    version = BridgeProtocol.Version,
                    type = "result",
                    action.RequestId,
                    action = action.Type,
                    sentMessageIds = sentIds,
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disconnect cancels queued and in-flight work; an in-flight send may already have reached Discord.
        }
        catch (Exception exception)
        {
            int? statusCode = exception is HttpException httpException ? (int)httpException.HttpCode : null;
            var definitiveRejection = statusCode is >= 400 and < 500;
            var outcomeUnknown = dispatching && !definitiveRejection;
            var code = sentIds.Count > 0 ? "partial_send" : "discord_error";

            _logger.Warning(
                "Bridge action {Action} failed in guild {GuildId}, channel {ChannelId}, message {MessageId}; sent {SentCount}; unknown outcome {OutcomeUnknown}; HTTP {StatusCode}; {ErrorType}",
                action.Type,
                origin.GuildId,
                origin.ChannelId,
                origin.MessageId,
                sentIds.Count,
                outcomeUnknown,
                statusCode,
                exception.GetType().Name);

            EnqueueError(
                action.RequestId,
                code,
                "Discord action failed. Do not automatically retry unknown outcomes.",
                sentIds,
                outcomeUnknown);
        }
    }

    private void EnqueueError(
        string? requestId,
        string code,
        string message,
        List<string>? sentIds = null,
        bool outcomeUnknown = false)
    {
        Enqueue(
            new
            {
                version = BridgeProtocol.Version,
                type = "error",
                requestId,
                code,
                message,
                sentMessageIds = sentIds ?? [],
                outcomeUnknown,
            });
    }

    private void Enqueue(object frame)
    {
        if (_outbound.Writer.TryWrite(new Outbound(frame, Origin: null)))
            return;

        _logger.Warning("Bridge output queue full; disconnecting client to avoid losing an action result");

        throw new WebSocketException("Bridge output queue is full.");
    }

    private static async Task SendAsync(WebSocket socket, object frame, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, BridgeProtocol.JsonOptions);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
    }

    private sealed record Outbound
    {
        public Outbound(object Frame, BridgeOrigin? Origin)
        {
            this.Frame = Frame;
            this.Origin = Origin;
        }

        public object Frame { get; init; }
        public BridgeOrigin? Origin { get; init; }

        public void Deconstruct(out object Frame, out BridgeOrigin? Origin)
        {
            Frame = this.Frame;
            Origin = this.Origin;
        }
    }
}