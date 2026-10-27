namespace SylvanBot.Features.SylvanMail;

using System.Globalization;
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
    private readonly Channel<SylvanMailMessage> _messages = Channel.CreateBounded<SylvanMailMessage>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public Task InitializeAsync(CancellationToken ct)
    {
        client.MessageReceived += ReceivedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.MessageReceived -= ReceivedAsync;
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
                    await workflow.RelayAsync(message, timeout.Token);
                }
                catch (Exception exception)
                {
                    logger.Warning(
                        "SylvanMail handling failed for source {SourceId}: {ExceptionType}",
                        message.SourceMessageId,
                        exception.GetType().Name);

                    if (!stoppingToken.IsCancellationRequested && message.GuildId is null)
                        await discord.ReportAsync(
                            message,
                            message.GuildId is null ? null : message.SourceChannelId,
                            null,
                            "SylvanMail processing failed or was interrupted. Delivery is unconfirmed; do not assume it arrived. No automatic resend.",
                            stoppingToken);
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
        if (source.Author.IsBot
            || source.Author.IsWebhook
            || source.Source != MessageSource.User
            || source is not SocketUserMessage message
            || source.Type is not (MessageType.Default or MessageType.Reply))
            return Task.CompletedTask;

        var isDm = source.Channel is IDMChannel;
        var thread = source.Channel as SocketThreadChannel;

        if (!isDm && thread is null)
            return Task.CompletedTask;

        var content = source.Content ?? string.Empty;

        if (!isDm)
        {
            var mention = $"<@{client.CurrentUser.Id}>";
            var nicknameMention = $"<@!{client.CurrentUser.Id}>";

            var explicitlyMentioned = content.Contains(mention, StringComparison.Ordinal)
                                      || content.Contains(nicknameMention, StringComparison.Ordinal);

            if (!explicitlyMentioned)
                return Task.CompletedTask;

            content = content.Replace(mention, string.Empty, StringComparison.Ordinal)
                .Replace(nicknameMention, string.Empty, StringComparison.Ordinal);
        }

        ImmutableArray<SylvanMailAttachment> attachments = source.Attachments
            .Select(item => new SylvanMailAttachment(item.Url, item.Filename, item.Size, item.IsSpoiler()))
            .ToImmutableArray();

        var unsupported = message.Stickers.Count > 0
                          || message.Components.Count > 0
                          || message.Poll is not null
                          || message.Flags?.HasFlag(MessageFlags.VoiceMessage) == true
                          || message.Reference?.ReferenceType is { IsSpecified: true, Value: MessageReferenceType.Forward }
                          || (string.IsNullOrWhiteSpace(content) && attachments.IsEmpty && message.Embeds.Count > 0);

        // Escape only the presentation label; relay text is kept verbatim except Sylvan's own mention.
        var name = Format.Sanitize(source.Author.Username);
        var timestamp = source.Timestamp.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var prefix = $"[{timestamp}] {name}: ";

        var input = new SylvanMailMessage(
            source.Id,
            source.Channel.Id,
            source.Author.Id,
            prefix,
            source.Author.Username,
            thread?.Guild.Id,
            content,
            attachments,
            unsupported);

        return !_messages.Writer.TryWrite(input)
            ? RefuseBusyAsync(input)
            : Task.CompletedTask;
    }

    private async Task RefuseBusyAsync(SylvanMailMessage message)
    {
        // Refuse overload explicitly without blocking gateway dispatch behind a slow delivery.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await workflow.RefuseBusyAsync(message, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.Warning("SylvanMail overload refusal failed for source {SourceId}: {ExceptionType}",
                message.SourceMessageId, exception.GetType().Name);
        }
    }
}
