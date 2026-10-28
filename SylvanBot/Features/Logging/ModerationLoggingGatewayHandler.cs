using System.Threading.Channels;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Features.MessageCache;
using SylvanBot.Gateway;

namespace SylvanBot.Features.Logging;

public sealed class ModerationLoggingGatewayHandler(
    DiscordSocketClient discordClient,
    LoggingScopeResolver scope,
    ModerationLogPublisher publisher,
    MessageSnapshotCache cache,
    MessageUpdateGatewayAdapter updatePayload,
    ILogger logger
) : BackgroundService, IDiscordGatewayHandler
{
    private readonly ILogger _logger = logger.ForContext<ModerationLoggingGatewayHandler>();

    private readonly Channel<CacheWork> _work = Channel.CreateBounded<CacheWork>(
        new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private CancellationToken _stoppingToken;
    private long _failures;

    public long Failures => Interlocked.Read(ref _failures);

    public Task InitializeAsync(CancellationToken ct)
    {
        discordClient.MessageReceived += DispatchMessageReceivedAsync;
        discordClient.MessageUpdated += DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted += DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted += DispatchMessagesBulkDeletedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.MessageReceived -= DispatchMessageReceivedAsync;
        discordClient.MessageUpdated -= DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted -= DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted -= DispatchMessagesBulkDeletedAsync;
        _work.Writer.TryComplete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                try
                {
                    await ProcessAsync(work: work, ct: timeout.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _failures);

                    _logger.Warning(
                        "Message handling timed out for guild {GuildId}, channel {ChannelId}",
                        propertyValue0: work.GuildId,
                        propertyValue1: work.ChannelId);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref _failures);

                    _logger.Warning(
                        "Message cache handling failed for guild {GuildId}, channel {ChannelId}, message {MessageId}: {ExceptionType}",
                        work.GuildId,
                        work.ChannelId,
                        work switch
                        {
                            ReceivedWork received => received.Message.MessageId,
                            EditedWork edited => edited.Message.MessageId,
                            _ => (ulong?)null,
                        },
                        exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Already persisted observations survive shutdown; pending events are transient and are not replayed.
        }
    }

    private static bool IsQualifyingUserMessage(IMessage message) =>
        message.Source == MessageSource.User && !message.Author.IsBot && !message.Author.IsWebhook;

    private Task DispatchMessageReceivedAsync(SocketMessage message)
    {
        if (!IsQualifyingUserMessage(message) || message.Channel is not IGuildChannel guildChannel)
            return Task.CompletedTask;

        ImmutableArray<MessageAttachment> attachments = message
           .Attachments
           .Select(attachment => new MessageAttachment(
                       Url: attachment.Url,
                       ProxyUrl: attachment.ProxyUrl,
                       ContentType: attachment.ContentType,
                       Size: attachment.Size))
           .ToImmutableArray();

        var valid = attachments
           .All(attachment => !string.IsNullOrWhiteSpace(attachment.Url) && attachment.Size >= 0);

        if (!valid || message.Content is not { } content)
        {
            Interlocked.Increment(ref _failures);
            _logger.Warning("Invalid received message metadata for {MessageId}; cache observation skipped", propertyValue: message.Id);

            return Task.CompletedTask;
        }

        var received = new MessageReceived
        {
            GuildId = guildChannel.GuildId,
            ChannelId = message.Channel.Id,
            MessageId = message.Id,
            AuthorId = message.Author.Id,
            CreatedAtUtc = SnowflakeUtils.FromSnowflake(message.Id),
            Eligible = true,
            Content = content,
            Attachments = attachments,
            EditedAtUtc = message.EditedTimestamp?.ToUniversalTime(),
        };

        return QueueAsync(new ReceivedWork(received));
    }

    private Task DispatchMessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (after is not SocketUserMessage || channel is not IGuildChannel guildChannel)
            return Task.CompletedTask;

        // Never infer absence from Discord.NET's detached defaults (notably the empty attachment array).
        DotNext.Optional<MessageEdited> observation = updatePayload.Current;

        if (observation is not { HasValue: true, Value: var payload })
            return Task.CompletedTask;

        var matchesEvent = payload.MessageId == after.Id && payload.ChannelId == channel.Id && payload.GuildId == guildChannel.GuildId;

        if (!matchesEvent)
        {
            Interlocked.Increment(ref _failures);
            _logger.Warning("Message update field presence unavailable for {MessageId}; update skipped", propertyValue: after.Id);

            return Task.CompletedTask;
        }

        var edited = payload with
        {
            GuildId = guildChannel.GuildId,
            ChannelId = channel.Id,
            AuthorId = after.Author.Id == 0 ? default : new DotNext.Optional<ulong>(after.Author.Id),
            Eligible = after.Author.Id == 0 ? default : new DotNext.Optional<bool>(IsQualifyingUserMessage(after)),
        };

        return QueueAsync(new EditedWork(edited));
    }

    private Task DispatchMessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel) =>
        QueueDeleteAsync(channelId: channel.Id, [message.Id], bulk: false);

    private Task DispatchMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
    {
        ImmutableArray<ulong> ids = messages
           .Select(message => message.Id)
           .ToImmutableArray();

        return QueueDeleteAsync(channelId: channel.Id, ids: ids, bulk: true);
    }

    private Task QueueDeleteAsync(ulong channelId, ImmutableArray<ulong> ids, bool bulk)
    {
        if (discordClient.GetChannel(channelId) is not IGuildChannel channel)
            return Task.CompletedTask;

        return QueueAsync(
            new DeletionWork(GuildId: channel.GuildId, ChannelId: channelId, DeletedMessageIds: ids, DeletedAt: DateTimeOffset.UtcNow, Bulk: bulk));
    }

    private async Task QueueAsync(CacheWork work)
    {
        try
        {
            // Await admission only. Sustained overload applies bounded backpressure to gateway dispatch.
            await _work.Writer.WriteAsync(item: work, cancellationToken: _stoppingToken);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
            // Shutdown has stopped admission.
        }
    }

    private async Task ProcessAsync(CacheWork work, CancellationToken ct)
    {
        if (
            discordClient.GetChannel(work.ChannelId) is not IMessageChannel sourceChannel
            || sourceChannel is not IGuildChannel guildChannel
            || guildChannel.GuildId != work.GuildId)
            return;

        if (work is DeletionWork deletion)
        {
            await ProcessDeletionAsync(work: deletion, sourceChannel: sourceChannel, ct: ct);

            return;
        }

        var configuration = await scope.GetConfigurationAsync(guildId: work.GuildId, sourceChannel: sourceChannel, ct: ct);

        if (configuration is null)
            return;

        switch (work)
        {
            case ReceivedWork received:
                await cache.ReceiveAsync(message: received.Message, ct: ct);

                break;
            case EditedWork edited:
                await ProcessEditAsync(edited: edited.Message, sourceChannel: sourceChannel, ct: ct);

                break;
        }
    }

    private async Task ProcessEditAsync(MessageEdited edited, IMessageChannel sourceChannel, CancellationToken ct)
    {
        DotNext.Optional<MessageEdit> result = await cache.EditAsync(message: edited, ct: ct);

        if (result is not { HasValue: true, Value: var change })
            return;

        var after = change.After;
        var beforeContent = change.Before is { HasValue: true, Value: var before } ? before.Content : null;

        var contentChanged = edited.Content is { HasValue: true }
            && !string.Equals(a: beforeContent, b: after.Content, comparisonType: StringComparison.Ordinal);

        if (contentChanged)
        {
            await publisher.PublishEditAsync(
                guildId: edited.GuildId,
                sourceChannel: sourceChannel,
                authorId: after.AuthorId,
                $"https://discord.com/channels/{edited.GuildId}/{edited.ChannelId}/{after.MessageId}",
                beforeContent: beforeContent,
                afterContent: after.Content,
                expiresAtUtc: after.ExpiresAtUtc,
                ct: ct);
        }
    }

    private async Task ProcessDeletionAsync(DeletionWork work, IMessageChannel sourceChannel, CancellationToken ct)
    {
        ImmutableArray<CachedMessageSnapshot> snapshots = await cache.BeginDeleteAsync(
            guildId: work.GuildId,
            messageIds: work.DeletedMessageIds,
            ct: ct);

        if (snapshots.IsEmpty)
            return;

        bool published;

        if (work.Bulk)
        {
            var authors = snapshots
               .Select(message => message.AuthorId)
               .Distinct()
               .Take(2)
               .ToArray();

            // Uncached IDs may belong to another author, including an excluded bot or webhook.
            var soleAuthorKnown = authors.Length == 1 && snapshots.Length == work.DeletedMessageIds.Length;

            published = await publisher.PublishBulkDeletionAsync(
                messages: snapshots,
                sourceChannel: sourceChannel,
                originalMessageCount: work.DeletedMessageIds.Length,
                soleAuthorKnown ? authors[0] : null,
                deletedAt: work.DeletedAt,
                ct: ct);
        }
        else
            published = await publisher.PublishDeletionAsync(guildId: work.GuildId, sourceChannel: sourceChannel, snapshots[0], ct: ct);

        if (published)
            await cache.CompleteDeleteAsync(snapshots: snapshots, ct: ct);
    }

    private abstract record CacheWork(ulong GuildId, ulong ChannelId);

    private sealed record ReceivedWork(MessageReceived Message) : CacheWork(GuildId: Message.GuildId, ChannelId: Message.ChannelId);

    private sealed record EditedWork(MessageEdited Message) : CacheWork(GuildId: Message.GuildId, ChannelId: Message.ChannelId);

    private sealed record DeletionWork(ulong GuildId, ulong ChannelId, ImmutableArray<ulong> DeletedMessageIds, DateTimeOffset DeletedAt, bool Bulk)
        : CacheWork(GuildId: GuildId, ChannelId: ChannelId);
}