namespace SylvanBot.Features.SylvanMail;

using System.Threading.Channels;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Gateway;

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
            FullMode = BoundedChannelFullMode.Wait,
        });

    public Task InitializeAsync(CancellationToken ct)
    {
        client.MessageReceived += ReceivedAsync;
        client.MessageUpdated += UpdatedAsync;
        client.ReactionAdded += ReactionAddedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.MessageReceived -= ReceivedAsync;
        client.MessageUpdated -= UpdatedAsync;
        client.ReactionAdded -= ReactionAddedAsync;
        _messages.Writer.TryComplete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    if (message.Inbound is not null)
                        await workflow.RelayAsync(message.Inbound, timeout.Token);
                    else
                        await workflow.HandleDraftAsync(message.ChannelId, message.MessageId, message.AuthorId, message.Confirm, timeout.Token);
                }
                catch (Exception exception)
                {
                    logger.Warning(
                        "SylvanMail handling failed for source {SourceId}: {ExceptionType}",
                        message.MessageId,
                        exception.GetType().Name);

                    if (!stoppingToken.IsCancellationRequested && message.Inbound is not null)
                    {
                        using var noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await discord.ReportAsync(
                            message.Inbound,
                            null,
                            null,
                            "SylvanMail processing failed or was interrupted. Delivery is unconfirmed; do not assume it arrived. No automatic resend.",
                            noticeTimeout.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Undelivered in-memory events are not replayed on startup. Saved attempts remain available for reconciliation.
        }
    }

    private Task ReceivedAsync(SocketMessage source)
    {
        if (source is not IUserMessage message || !IsUserMessage(message))
            return Task.CompletedTask;

        if (source.Channel is IDMChannel)
        {
            var input = SylvanMailDiscord.ReadMessage(message, guildId: null);
            var work = new MailWork(source.Channel.Id, source.Id, source.Author.Id, false, input);

            return !_messages.Writer.TryWrite(work) ? RefuseBusyAsync(input) : Task.CompletedTask;
        }

        return QueueDraftAsync(message, edited: false);
    }

    private Task UpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        // Edits only refresh the draft marker. They can never send a reply.
        return after is IUserMessage message && IsUserMessage(message) ? QueueDraftAsync(message, edited: true) : Task.CompletedTask;
    }

    private Task QueueDraftAsync(IUserMessage message, bool edited)
    {
        if (message.Channel is not IThreadChannel)
            return Task.CompletedTask;

        var hasBodyMention = SylvanMailReply.Parse(message.Content ?? string.Empty, client.CurrentUser.Id, false, false) is not null;
        var hasReplyPing = message.Type == MessageType.Reply && message.MentionedUserIds.Contains(client.CurrentUser.Id);

        if (!edited && !hasBodyMention && !hasReplyPing)
            return Task.CompletedTask;

        // The workflow checks the mapped forum before fetching any content.
        _messages.Writer.TryWrite(new MailWork(message.Channel.Id, message.Id, message.Author.Id, false, null));

        return Task.CompletedTask;
    }

    private Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (reaction.UserId == client.CurrentUser.Id || !string.Equals(reaction.Emote.Name, SylvanMailDiscord.SendEmoji, StringComparison.Ordinal))
            return Task.CompletedTask;

        var work = new MailWork(channel.Id, message.Id, reaction.UserId, true, null);

        return !_messages.Writer.TryWrite(work)
            ? RefuseConfirmationAsync(work)
            : Task.CompletedTask;
    }

    private static bool IsUserMessage(IUserMessage message) =>
        !message.Author.IsBot && !message.Author.IsWebhook && message.Source == MessageSource.User
        && message.Type is MessageType.Default or MessageType.Reply;

    private async Task RefuseConfirmationAsync(MailWork work)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await workflow.RefuseConfirmationAsync(work.ChannelId, work.MessageId, work.AuthorId, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning("SylvanMail confirmation refusal failed for source {SourceId}: {ExceptionType}",
                work.MessageId, exception.GetType().Name);
        }
    }

    private async Task RefuseBusyAsync(SylvanMailMessage message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await workflow.RefuseBusyAsync(message, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning("SylvanMail overload refusal failed for source {SourceId}: {ExceptionType}",
                message.SourceMessageId, exception.GetType().Name);
            await discord.ReportAsync(message, null, null, "Not relayed: SylvanMail is temporarily busy. Please try again later.", timeout.Token);
        }
    }

    private sealed record MailWork(ulong ChannelId, ulong MessageId, ulong AuthorId, bool Confirm, SylvanMailMessage? Inbound);
}
