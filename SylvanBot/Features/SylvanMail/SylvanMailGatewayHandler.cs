using System.Threading.Channels;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Gateway;

namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailGatewayHandler(
    DiscordSocketClient client,
    SylvanMailWorkflow workflow,
    ISylvanMailDiscord discord,
    ILogger logger)
    : BackgroundService, IDiscordGatewayHandler
{
    // Bounded, transient work only. This is deliberately not a durable queue or a retry worker.
    private readonly Channel<MailWork> _messages = Channel.CreateBounded<MailWork>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode     = BoundedChannelFullMode.Wait,
        });

    public Task InitializeAsync(CancellationToken ct)
    {
        client.MessageReceived += ReceivedAsync;
        client.MessageUpdated  += UpdatedAsync;
        client.ReactionAdded   += ReactionAddedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.MessageReceived -= ReceivedAsync;
        client.MessageUpdated  -= UpdatedAsync;
        client.ReactionAdded   -= ReactionAddedAsync;
        _messages.Writer.TryComplete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken))
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));

                    if (message.Inbound is not null)
                        await workflow.RelayAsync(message: message.Inbound, ct: timeout.Token);
                    else
                        await workflow.HandleDraftAsync(threadId: message.ChannelId, messageId: message.MessageId, authorId: message.AuthorId, confirm: message.Confirm, ct: timeout.Token);
                }
                catch (Exception exception)
                {
                    logger.Warning(
                        "SylvanMail handling failed for source {SourceId} in channel {ChannelId} by author {AuthorId}: {ExceptionType}",
                        message.MessageId,
                        message.ChannelId,
                        message.AuthorId,
                        exception.GetType().Name);

                    if (!stoppingToken.IsCancellationRequested && message.Inbound is not null)
                    {
                        using var noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                        await discord.ReportAsync(
                            source: message.Inbound,
                            threadId: null,
                            operation: null,
                            "SylvanMail processing failed or was interrupted. Delivery is unconfirmed; do not assume it arrived. No automatic resend.",
                            ct: noticeTimeout.Token);
                    }
                }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Undelivered in-memory events are not replayed on startup. Saved attempts remain available for reconciliation.
        }
    }

    private static bool IsUserMessage(IUserMessage message) =>
        message is { Author.IsBot: false, Author.IsWebhook: false, Source: MessageSource.User, Type: MessageType.Default or MessageType.Reply };

    private Task ReceivedAsync(SocketMessage source)
    {
        if (source is not IUserMessage message || !IsUserMessage(message))
            return Task.CompletedTask;

        if (source.Channel is not IDMChannel)
            return QueueDraftAsync(message: message, edited: false);

        var input = SylvanMailDiscord.ReadMessage(source: message, guildId: null);
        var work  = new MailWork(ChannelId: source.Channel.Id, MessageId: source.Id, AuthorId: source.Author.Id, Confirm: false, Inbound: input);

        return !_messages.Writer.TryWrite(work) ? RefuseBusyAsync(input) : Task.CompletedTask;
    }

    private Task UpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel) =>
        // Edits only refresh the draft marker. They can never send a reply.
        after is IUserMessage message && IsUserMessage(message) ? QueueDraftAsync(message: message, edited: true) : Task.CompletedTask;

    private Task QueueDraftAsync(IUserMessage message, bool edited)
    {
        if (message.Channel is not IThreadChannel)
            return Task.CompletedTask;

        var hasBodyMention = SylvanMailReply.Parse(message.Content ?? string.Empty, botId: client.CurrentUser.Id, replyToBot: false, replyPingEnabled: false) is not null;
        var hasReplyPing   = message.Type == MessageType.Reply && message.MentionedUserIds.Contains(client.CurrentUser.Id);

        if (!edited && !hasBodyMention && !hasReplyPing)
            return Task.CompletedTask;

        // The workflow checks the mapped forum before fetching any content.
        _messages.Writer.TryWrite(new MailWork(ChannelId: message.Channel.Id, MessageId: message.Id, AuthorId: message.Author.Id, Confirm: false, Inbound: null));

        return Task.CompletedTask;
    }

    private Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (reaction.UserId == client.CurrentUser.Id || !string.Equals(a: reaction.Emote.Name, b: SylvanMailDiscord.SendEmoji, comparisonType: StringComparison.Ordinal))
            return Task.CompletedTask;

        var work = new MailWork(ChannelId: channel.Id, MessageId: message.Id, AuthorId: reaction.UserId, Confirm: true, Inbound: null);

        return !_messages.Writer.TryWrite(work)
            ? RefuseConfirmationAsync(work)
            : Task.CompletedTask;
    }

    private async Task RefuseConfirmationAsync(MailWork work)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await workflow.RefuseConfirmationAsync(threadId: work.ChannelId, messageId: work.MessageId, authorId: work.AuthorId, ct: timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning(
                "SylvanMail confirmation refusal failed for source {SourceId}: {ExceptionType}",
                propertyValue0: work.MessageId,
                propertyValue1: exception.GetType().Name);
        }
    }

    private async Task RefuseBusyAsync(SylvanMailMessage message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await workflow.RefuseBusyAsync(message: message, ct: timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning(
                "SylvanMail overload refusal failed for source {SourceId}: {ExceptionType}",
                propertyValue0: message.SourceMessageId,
                propertyValue1: exception.GetType().Name);
            await discord.ReportAsync(source: message, threadId: null, operation: null, "Not relayed: SylvanMail is temporarily busy. Please try again later.", ct: timeout.Token);
        }
    }

    private sealed record MailWork(ulong ChannelId, ulong MessageId, ulong AuthorId, bool Confirm, SylvanMailMessage? Inbound);
}