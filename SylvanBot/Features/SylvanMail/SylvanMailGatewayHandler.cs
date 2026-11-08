using System.Threading.Channels;
using System.Diagnostics;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Gateway;
using static System.StringComparison;
using static SylvanBot.Features.SylvanMail.SylvanMailDrafts;

namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailGatewayHandler(
    DiscordSocketClient client,
    SylvanMailClient mail,
    SylvanMailWorkflow workflow,
    SylvanMailDraftWorkflow drafts,
    ISylvanMailDiscord discord,
    ILogger logger) : BackgroundService, IDiscordGatewayHandler
{
    // Bounded, transient work only. This is deliberately not a durable queue or a retry worker.
    private readonly Channel<MailWork> _messages = Channel.CreateBounded<MailWork>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public Task InitializeAsync(CancellationToken ct)
    {
        client.MessageReceived += ReceivedAsync;
        mail.Client.MessageReceived += DmReceivedAsync;
        client.MessageUpdated += UpdatedAsync;
        client.ReactionAdded += ReactionAddedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.MessageReceived -= ReceivedAsync;
        StopMailAdmission();
        client.MessageUpdated -= UpdatedAsync;
        client.ReactionAdded -= ReactionAddedAsync;
        _messages.Writer.TryComplete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<Task> pending = [];
        Dictionary<ulong, Task> conversations = new Dictionary<ulong, Task>();
        using var workers = new SemaphoreSlim(8, 8);

        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken))
            {
                if (pending.Count >= 64)
                    await Task.WhenAny(pending).WaitAsync(stoppingToken);

                pending.RemoveAll(task => task.IsCompleted);

                var completed = conversations
                   .Where(pair => pair.Value.IsCompleted)
                   .Select(pair => pair.Key)
                   .ToArray();

                foreach (var user in completed)
                    conversations.Remove(user);

                try
                {
                    using var routingTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    routingTimeout.CancelAfter(TimeSpan.FromSeconds(10));

                    DotNext.Optional<ulong> recipient = message is InboundMail inbound
                        ? new DotNext.Optional<ulong>(inbound.AuthorId)
                        : await drafts.ConversationUserAsync(message.ChannelId, routingTimeout.Token);

                    if (recipient is not { HasValue: true, Value: var userId })
                        continue;

                    var previous = conversations.GetValueOrDefault(userId, Task.CompletedTask);
                    var task = ProcessAfterAsync(message, previous, workers, stoppingToken);
                    conversations[userId] = task;
                    pending.Add(task);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.Warning(
                        "SylvanMail routing failed for source {SourceId} in channel {ChannelId} by author {AuthorId}: {ExceptionType}",
                        message.MessageId,
                        message.ChannelId,
                        message.AuthorId,
                        exception.GetType().Name);

                    if (!stoppingToken.IsCancellationRequested && message is InboundMail inbound)
                        await ReportFailureAsync(inbound.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Undelivered in-memory events are not replayed on startup. Saved attempts remain available for reconciliation.
        }
        finally
        {
            // Every dispatched operation remains owned through shutdown.
            await Task.WhenAll(pending);
        }
    }

    private async Task ProcessAfterAsync(MailWork message, Task previous, SemaphoreSlim workers, CancellationToken stoppingToken)
    {
        using var sourceScope = Serilog.Context.LogContext.PushProperty("SourceId", message.MessageId);
        using var channelScope = Serilog.Context.LogContext.PushProperty("ChannelId", message.ChannelId);
        using var operationScope = Serilog.Context.LogContext.PushProperty("MailOperation", message.GetType().Name);
        var started = Stopwatch.GetTimestamp();
        var queueMs = 0.0;
        var acquired = false;
        var outcome = "completed";

        try
        {
            await previous;
            await workers.WaitAsync(stoppingToken);
            acquired = true;
            queueMs = Stopwatch.GetElapsedTime(message.QueuedAt).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            if (message is InboundMail inbound)
            {
                await workflow.RelayAsync(inbound.Message, timeout.Token);
            }
            else
            {
                var action = message switch
                {
                    DraftConfirmed confirmation => confirmation.Action,
                    DraftCancelled => SylvanMailDraftAction.Cancel,
                    _ => SylvanMailDraftAction.Update,
                };

                await drafts.HandleDraftAsync(message.ChannelId, message.MessageId, message.AuthorId, action, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            outcome = "stopped";
        }
        catch (Exception exception)
        {
            outcome = exception.GetType().Name;

            logger.Warning(
                "SylvanMail handling failed for source {SourceId} in channel {ChannelId} by author {AuthorId}: {ExceptionType}",
                message.MessageId,
                message.ChannelId,
                message.AuthorId,
                outcome);

            if (!stoppingToken.IsCancellationRequested && message is InboundMail inbound)
                await ReportFailureAsync(inbound.Message);
        }
        finally
        {
            if (acquired)
                workers.Release();

            logger.Debug(
                "SylvanMail {Operation} for source {SourceId} in channel {ChannelId}: {Outcome}; queue {QueueMs} ms, execution {ExecutionMs} ms, total {TotalMs} ms",
                message.GetType().Name,
                message.MessageId,
                message.ChannelId,
                outcome,
                acquired ? queueMs : Stopwatch.GetElapsedTime(message.QueuedAt).TotalMilliseconds,
                acquired ? Stopwatch.GetElapsedTime(started).TotalMilliseconds : 0,
                Stopwatch.GetElapsedTime(message.QueuedAt).TotalMilliseconds);
        }
    }

    private static bool IsUserMessage(IUserMessage message) =>
        message is { Author.IsBot: false, Author.IsWebhook: false, Source: MessageSource.User, Type: MessageType.Default or MessageType.Reply };

    private Task ReceivedAsync(SocketMessage source)
    {
        if (source.Channel is IDMChannel || source is not IUserMessage message || !IsUserMessage(message))
            return Task.CompletedTask;

        return QueueDraftAsync(message: message, edited: false);
    }

    public void StopMailAdmission() => mail.Client.MessageReceived -= DmReceivedAsync;

    private Task DmReceivedAsync(SocketMessage source)
    {
        if (source.Channel is not IDMChannel || source is not IUserMessage message || !IsUserMessage(message))
            return Task.CompletedTask;

        var input = ReadMessage(source: message, guildId: null);

        if (!mail.IsReady)
            return RefuseBusyAsync(input, unavailable: true);

        var work = new InboundMail(input);

        return !_messages.Writer.TryWrite(work) ? RefuseBusyAsync(input) : Task.CompletedTask;
    }

    private Task UpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel) =>
        // Edits only refresh the draft marker. They can never send a reply.
        after is IUserMessage message
        && IsUserMessage(message)
            ? QueueDraftAsync(message: message, edited: true)
            : Task.CompletedTask;

    private Task QueueDraftAsync(IUserMessage message, bool edited)
    {
        if (message.Channel is not IThreadChannel)
            return Task.CompletedTask;

        var hasBodyMention =
            SylvanMailReply.Parse(message.Content ?? string.Empty, botId: client.CurrentUser.Id, replyToBot: false, replyPingEnabled: false)
                is not null;

        var hasReplyPing = message.Type == MessageType.Reply && message.MentionedUserIds.Contains(client.CurrentUser.Id);

        if (!edited && !hasBodyMention && !hasReplyPing)
            return Task.CompletedTask;

        // The workflow checks the mapped forum before fetching any content.
        _messages.Writer.TryWrite(new DraftUpdated(ChannelId: message.Channel.Id, MessageId: message.Id, AuthorId: message.Author.Id));

        return Task.CompletedTask;
    }

    private Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        var name = reaction.Emote.Name.Replace("\uFE0F", string.Empty, Ordinal);
        var anonymous = reaction.Emote is Emoji && string.Equals(name, AnonymousEmoji, Ordinal);
        var signed = reaction.Emote is Emoji && string.Equals(name, SignedEmoji, Ordinal);
        var cancel = string.Equals(reaction.Emote.Name, CancelEmoji, Ordinal);

        if (reaction.UserId == client.CurrentUser.Id || (!anonymous && !signed && !cancel))
            return Task.CompletedTask;

        MailWork work = cancel
            ? new DraftCancelled(ChannelId: channel.Id, MessageId: message.Id, AuthorId: reaction.UserId)
            : new DraftConfirmed(
                ChannelId: channel.Id,
                MessageId: message.Id,
                AuthorId: reaction.UserId,
                Action: signed ? SylvanMailDraftAction.SendSigned : SylvanMailDraftAction.SendAnonymous);

        return !_messages.Writer.TryWrite(work) ? RefuseConfirmationAsync(work) : Task.CompletedTask;
    }

    private async Task RefuseConfirmationAsync(MailWork work)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await drafts.RefuseConfirmationAsync(
                threadId: work.ChannelId,
                messageId: work.MessageId,
                authorId: work.AuthorId,
                action: work is DraftConfirmed confirmation ? confirmation.Action : SylvanMailDraftAction.Cancel,
                ct: timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning(
                "SylvanMail confirmation refusal failed for source {SourceId}: {ExceptionType}",
                propertyValue0: work.MessageId,
                propertyValue1: exception.GetType().Name);
        }
    }

    private async Task RefuseBusyAsync(SylvanMailMessage message, bool unavailable = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            if (unavailable)
                await workflow.RefuseUnavailableAsync(message: message, ct: timeout.Token);
            else
                await workflow.RefuseBusyAsync(message: message, ct: timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning(
                "SylvanMail overload refusal failed for source {SourceId}: {ExceptionType}",
                propertyValue0: message.SourceMessageId,
                propertyValue1: exception.GetType().Name);

            try
            {
                await discord.ReportAsync(
                    source: message,
                    threadId: default,
                    receiptId: default,
                    "Not relayed: SylvanMail is temporarily unavailable. Please try again later.",
                    ct: timeout.Token);
            }
            catch (Exception noticeException)
            {
                logger.Warning(
                    "SylvanMail overload notice failed for source {SourceId}: {ExceptionType}",
                    propertyValue0: message.SourceMessageId,
                    propertyValue1: noticeException.GetType().Name);
            }
        }
    }

    private async Task ReportFailureAsync(SylvanMailMessage message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await discord.ReportAsync(
                source: message,
                threadId: default,
                receiptId: default,
                "SylvanMail processing failed or was interrupted. Delivery is unconfirmed; do not assume it arrived. No automatic resend.",
                ct: timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning(
                "SylvanMail failure notice failed for source {SourceId}: {ExceptionType}",
                propertyValue0: message.SourceMessageId,
                propertyValue1: exception.GetType().Name);
        }
    }

    private abstract record MailWork(ulong ChannelId, ulong MessageId, ulong AuthorId)
    {
        public long QueuedAt { get; } = Stopwatch.GetTimestamp();
    }

    private sealed record InboundMail(SylvanMailMessage Message) : MailWork(
        ChannelId: Message.SourceChannelId,
        MessageId: Message.SourceMessageId,
        AuthorId: Message.AuthorId);

    private sealed record DraftUpdated(ulong ChannelId, ulong MessageId, ulong AuthorId)
        : MailWork(ChannelId: ChannelId, MessageId: MessageId, AuthorId: AuthorId);

    private sealed record DraftConfirmed(ulong ChannelId, ulong MessageId, ulong AuthorId, SylvanMailDraftAction Action)
        : MailWork(ChannelId: ChannelId, MessageId: MessageId, AuthorId: AuthorId);

    private sealed record DraftCancelled(ulong ChannelId, ulong MessageId, ulong AuthorId)
        : MailWork(ChannelId: ChannelId, MessageId: MessageId, AuthorId: AuthorId);
}