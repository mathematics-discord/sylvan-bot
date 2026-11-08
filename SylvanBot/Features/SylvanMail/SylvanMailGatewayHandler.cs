using System.Threading.Channels;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Gateway;

namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailGatewayHandler(
    DiscordSocketClient client,
    SylvanMailClient mail,
    SylvanMailWorkflow workflow,
    SylvanMailDraftWorkflow drafts,
    ISylvanMailDiscord discord,
    ILogger logger
) : BackgroundService, IDiscordGatewayHandler
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
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken))
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));

                    switch (message)
                    {
                        case InboundMail inbound:
                            await workflow.RelayAsync(message: inbound.Message, ct: timeout.Token);

                            break;
                        case DraftUpdated draft:
                            await drafts.HandleDraftAsync(
                                threadId: draft.ChannelId,
                                messageId: draft.MessageId,
                                authorId: draft.AuthorId,
                                action: SylvanMailDraftAction.Update,
                                ct: timeout.Token);

                            break;
                        case DraftConfirmed confirmation:
                            await drafts.HandleDraftAsync(
                                threadId: confirmation.ChannelId,
                                messageId: confirmation.MessageId,
                                authorId: confirmation.AuthorId,
                                action: confirmation.Action,
                                ct: timeout.Token);

                            break;
                        case DraftCancelled cancellation:
                            await drafts.HandleDraftAsync(
                                threadId: cancellation.ChannelId,
                                messageId: cancellation.MessageId,
                                authorId: cancellation.AuthorId,
                                action: SylvanMailDraftAction.Cancel,
                                ct: timeout.Token);

                            break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.Warning(
                        "SylvanMail handling failed for source {SourceId} in channel {ChannelId} by author {AuthorId}: {ExceptionType}",
                        message.MessageId,
                        message.ChannelId,
                        message.AuthorId,
                        exception.GetType().Name);

                    if (!stoppingToken.IsCancellationRequested && message is InboundMail inbound)
                        await ReportFailureAsync(inbound.Message);
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
        if (source.Channel is IDMChannel || source is not IUserMessage message || !IsUserMessage(message))
            return Task.CompletedTask;

        return QueueDraftAsync(message: message, edited: false);
    }

    public void StopMailAdmission() => mail.Client.MessageReceived -= DmReceivedAsync;

    private Task DmReceivedAsync(SocketMessage source)
    {
        if (source.Channel is not IDMChannel || source is not IUserMessage message || !IsUserMessage(message))
            return Task.CompletedTask;

        var input = SylvanMailDrafts.ReadMessage(source: message, guildId: null);

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
        var name = reaction.Emote.Name.Replace("\uFE0F", string.Empty, StringComparison.Ordinal);
        var anonymous = reaction.Emote is Emoji && name == SylvanMailDrafts.AnonymousEmoji;
        var signed = reaction.Emote is Emoji && name == SylvanMailDrafts.SignedEmoji;
        var cancel = string.Equals(a: reaction.Emote.Name, b: SylvanMailDrafts.CancelEmoji, comparisonType: StringComparison.Ordinal);

        if (reaction.UserId == client.CurrentUser.Id || (!anonymous && !signed && !cancel))
            return Task.CompletedTask;

        MailWork work = cancel
            ? new DraftCancelled(ChannelId: channel.Id, MessageId: message.Id, AuthorId: reaction.UserId)
            : new DraftConfirmed(ChannelId: channel.Id, MessageId: message.Id, AuthorId: reaction.UserId,
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

    private abstract record MailWork(ulong ChannelId, ulong MessageId, ulong AuthorId);

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
