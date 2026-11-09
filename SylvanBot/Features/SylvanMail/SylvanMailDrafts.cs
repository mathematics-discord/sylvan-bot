using SylvanBot.Application.Features.SylvanMail;
using static SylvanBot.Features.SylvanMail.SylvanMailRequests;

namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailDrafts(DiscordSocketClient client, ILogger logger) : ISylvanMailDrafts
{
    public const string AnonymousEmoji = "\U0001F574";
    public const string SignedEmoji = "\U0001F9CD";
    public const string CancelEmoji = "\u274C";

    // Consecutive reaction changes share Discord's rate limits. Wait within the caller's
    // cancellation budget; this policy must not be used for sending message payloads.
    private RequestOptions ReactionOptions(CancellationToken ct) =>
        new RequestOptions
        {
            CancelToken = ct,
            RetryMode = RetryMode.RetryRatelimit,
            // Prefer Discord's precise relative reset over the SDK's HTTP-Date lag estimate.
            UseSystemClock = false,
            RatelimitCallback = info =>
            {
                if (info.Remaining == 0 || info.IsGlobal)
                    logger.Debug(
                        "SylvanMail reaction request exhausted rate bucket {Bucket}: limit {Limit}, remaining {Remaining}, reset after {ResetAfterMs} ms, retry after {RetryAfter}, global {Global}, estimated lag {LagMs} ms",
                        info.Bucket,
                        info.Limit,
                        info.Remaining,
                        info.ResetAfter?.TotalMilliseconds,
                        info.RetryAfter,
                        info.IsGlobal,
                        info.Lag?.TotalMilliseconds);

                return Task.CompletedTask;
            },
        };

    public static SylvanMailMessage ReadMessage(IUserMessage source, ulong? guildId)
    {
        ImmutableArray<SylvanMailAttachment> attachments = source
           .Attachments
           .Select(item => new SylvanMailAttachment(Url: item.Url, Filename: item.Filename, Size: item.Size, item.IsSpoiler()))
           .ToImmutableArray();

        var unsupported =
            source.Stickers.Count > 0
            || source.Components.Count > 0
            || source.Poll is not null
            || source.Flags?.HasFlag(MessageFlags.VoiceMessage) == true
            || source.Reference?.ReferenceType is { IsSpecified: true, Value: MessageReferenceType.Forward }
            || (string.IsNullOrWhiteSpace(source.Content) && attachments.IsEmpty && source.Embeds.Count > 0);

        return new SylvanMailMessage(
            SourceMessageId: source.Id,
            SourceChannelId: source.Channel.Id,
            AuthorId: source.Author.Id,
            Timestamp: source.Timestamp,
            Username: source.Author.Username,
            GuildId: guildId,
            source.Content ?? string.Empty,
            Attachments: attachments,
            UnsupportedContent: unsupported);
    }

    public async Task<DotNext.Optional<SylvanMailMessage>> ReadDraftAsync(
        ulong threadId,
        ulong messageId,
        ulong authorId,
        SylvanMailDraftAction action,
        CancellationToken ct)
    {
        var channel = await MessageChannelAsync(client: client, channelId: threadId, ct: ct, retryMode: RetryMode.RetryRatelimit);

        if (channel is not IThreadChannel thread)
            return default;

        var fetched = await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, ReactionOptions(ct));

        if (fetched is not IUserMessage source)
            return default;

        var authorMatches = source.Author.Id == authorId && !source.Author.IsBot && !source.Author.IsWebhook;
        var supportedType = source.Type is MessageType.Default or MessageType.Reply;

        if (!authorMatches || !supportedType)
            return default;

        var control = action == SylvanMailDraftAction.SendSigned ? SignedEmoji
            : action == SylvanMailDraftAction.Cancel ? CancelEmoji : AnonymousEmoji;

        var hasMarker = source.Reactions.TryGetValue(new Emoji(control), out var reaction) && reaction.IsMe;

        if (action != SylvanMailDraftAction.Update && !hasMarker)
            return default;

        var content = source.Content ?? string.Empty;
        var reply = SylvanMailReply.Parse(content: content, botId: client.CurrentUser.Id, replyToBot: false, replyPingEnabled: false);
        var hasReplyPing = source.Type == MessageType.Reply && source.MentionedUserIds.Contains(client.CurrentUser.Id);

        // A body mention prepares a draft without needing the referenced message.
        if (reply is null && hasReplyPing)
        {
            var replyToBot = source.ReferencedMessage?.Author.Id == client.CurrentUser.Id;

            if (!replyToBot && source.Reference?.MessageId.IsSpecified == true)
            {
                var referenced = await channel.GetMessageAsync(
                    id: source.Reference.MessageId.Value,
                    mode: CacheMode.AllowDownload,
                    ReactionOptions(ct));

                replyToBot = referenced?.Author.Id == client.CurrentUser.Id;
            }

            reply = SylvanMailReply.Parse(content: content, botId: client.CurrentUser.Id, replyToBot: replyToBot, replyPingEnabled: true);
        }

        if (reply is null)
        {
            await SetDraftReadyAsync(threadId: threadId, messageId: messageId, ready: false, ct: ct);

            return new DotNext.Optional<SylvanMailMessage>(ReadMessage(source: source, guildId: thread.GuildId));
        }

        var prefix = action != SylvanMailDraftAction.SendSigned ? "From staff: " : $"From {Format.Sanitize(source.Author.Username)}: ";

        return new DotNext.Optional<SylvanMailMessage>(
            ReadMessage(source: source, guildId: thread.GuildId) with
            {
                Content = reply.Content,
                OutboundPrefix = prefix,
            });
    }

    public async Task SetDraftReadyAsync(ulong threadId, ulong messageId, bool ready, CancellationToken ct)
    {
        using var timing = new OperationTiming(logger.ForContext("SourceId", messageId), "sylvanmail.draft_controls", "channel");

        try
        {
            var channel = await MessageChannelAsync(client: client, channelId: threadId, ct: ct, retryMode: RetryMode.RetryRatelimit);
            timing.Next("message");

            if (await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, ReactionOptions(ct)) is not IUserMessage message)
                return;

            var oldSend = new Emoji("\U0001F4E4");
            timing.Next("reactions");
            List<Task> changes = [];

            if (message.Reactions.TryGetValue(oldSend, out var oldReaction) && oldReaction.IsMe)
                changes.Add(ChangeReactionAsync(oldSend, add: false));

            foreach (var control in new[] { AnonymousEmoji, SignedEmoji, CancelEmoji })
            {
                var emoji = new Emoji(control);
                var marked = message.Reactions.TryGetValue(key: emoji, out var reaction) && reaction.IsMe;

                if (ready == marked)
                    continue;

                changes.Add(ChangeReactionAsync(emoji, ready));
            }

            await Task.WhenAll(changes);

            async Task ChangeReactionAsync(Emoji emoji, bool add)
            {
                using var reactionTiming = new OperationTiming(logger
                    .ForContext("SourceId", messageId)
                    .ForContext("Control", emoji.Name), "sylvanmail.draft_reaction", add ? "add" : "remove");

                if (add)
                    await message.AddReactionAsync(emote: emoji, ReactionOptions(ct));
                else
                    await message.RemoveReactionAsync(emote: emoji, userId: client.CurrentUser.Id, ReactionOptions(ct));
            }
        }
        catch (Exception exception)
        {
            LogFailure(logger: logger, "draft reaction", sourceId: messageId, exception: exception);
        }
    }

    public async Task ClearReactionsAsync(ulong threadId, ulong messageId, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(client: client, channelId: threadId, ct: ct, retryMode: RetryMode.RetryRatelimit);

        if (await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, ReactionOptions(ct)) is IUserMessage message)
            await message.RemoveAllReactionsAsync(ReactionOptions(ct));
    }

    public async Task MarkDeliveredAsync(SylvanMailMessage message, CancellationToken ct)
    {
        using var timing = new OperationTiming(logger.ForContext("SourceId", message.SourceMessageId), "sylvanmail.delivered_marker", "channel");

        try
        {
            var channel = await MessageChannelAsync(client: client, channelId: message.SourceChannelId, ct: ct, retryMode: RetryMode.RetryRatelimit);
            timing.Next("message");

            if (await channel.GetMessageAsync(id: message.SourceMessageId, mode: CacheMode.AllowDownload, ReactionOptions(ct)) is IUserMessage source)
            {
                timing.Next("clear_reactions");
                await source.RemoveAllReactionsAsync(ReactionOptions(ct));
                timing.Next("success_reaction");
                await source.AddReactionAsync(new Emoji("\u2705"), ReactionOptions(ct));
            }
        }
        catch (Exception exception)
        {
            // The payload was delivered; a failed reaction must never turn it into a failed delivery or cause a resend.
            LogFailure(logger: logger, "success reaction", sourceId: message.SourceMessageId, exception: exception);
        }
    }
}
