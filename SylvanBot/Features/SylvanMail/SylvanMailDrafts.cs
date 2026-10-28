using SylvanBot.Application.Features.SylvanMail;
using static SylvanBot.Features.SylvanMail.SylvanMailRequests;

namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailDrafts(DiscordSocketClient client, ILogger logger) : ISylvanMailDrafts
{
    public const string SendEmoji = "\U0001F4E4";
    public const string CancelEmoji = "\u274C";

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
        bool confirm,
        CancellationToken ct)
    {
        var channel = await MessageChannelAsync(client: client, channelId: threadId, ct: ct);

        if (channel is not IThreadChannel thread)
            return default;

        var fetched = await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, Options(ct));

        if (fetched is not IUserMessage source)
            return default;

        var authorMatches = source.Author.Id == authorId && !source.Author.IsBot && !source.Author.IsWebhook;
        var supportedType = source.Type is MessageType.Default or MessageType.Reply;

        if (!authorMatches || !supportedType)
            return default;

        var hasMarker = source.Reactions.TryGetValue(new Emoji(SendEmoji), out var reaction) && reaction.IsMe;

        if (confirm && !hasMarker)
            return default;

        var content = source.Content ?? string.Empty;
        var reply = SylvanMailReply.Parse(content: content, botId: client.CurrentUser.Id, replyToBot: false, replyPingEnabled: false);
        var hasReplyPing = source.Type == MessageType.Reply && source.MentionedUserIds.Contains(client.CurrentUser.Id);

        // An explicit body mention wins without depending on the referenced message still being accessible.
        if (reply is null && hasReplyPing)
        {
            var replyToBot = source.ReferencedMessage?.Author.Id == client.CurrentUser.Id;

            if (!replyToBot && source.Reference?.MessageId.IsSpecified == true)
            {
                var referenced = await channel.GetMessageAsync(id: source.Reference.MessageId.Value, mode: CacheMode.AllowDownload, Options(ct));
                replyToBot = referenced?.Author.Id == client.CurrentUser.Id;
            }

            reply = SylvanMailReply.Parse(content: content, botId: client.CurrentUser.Id, replyToBot: replyToBot, replyPingEnabled: true);
        }

        if (reply is null)
        {
            await SetDraftReadyAsync(threadId: threadId, messageId: messageId, ready: false, ct: ct);

            return new DotNext.Optional<SylvanMailMessage>(ReadMessage(source: source, guildId: thread.GuildId));
        }

        var prefix = reply.Anonymous ? "From staff: " : $"From {Format.Sanitize(source.Author.Username)}: ";

        return new DotNext.Optional<SylvanMailMessage>(
            ReadMessage(source: source, guildId: thread.GuildId) with
            {
                Content = reply.Content,
                OutboundPrefix = prefix,
            });
    }

    public async Task SetDraftReadyAsync(ulong threadId, ulong messageId, bool ready, CancellationToken ct)
    {
        try
        {
            var channel = await MessageChannelAsync(client: client, channelId: threadId, ct: ct);

            if (await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, Options(ct)) is not IUserMessage message)
                return;

            foreach (var control in new[] { SendEmoji, CancelEmoji })
            {
                var emoji = new Emoji(control);
                var marked = message.Reactions.TryGetValue(key: emoji, out var reaction) && reaction.IsMe;

                if (ready == marked)
                    continue;

                if (ready)
                    await message.AddReactionAsync(emote: emoji, Options(ct));
                else
                    await message.RemoveReactionAsync(emote: emoji, userId: client.CurrentUser.Id, Options(ct));
            }
        }
        catch (Exception exception)
        {
            LogFailure(logger: logger, "draft reaction", sourceId: messageId, exception: exception);
        }
    }

    public async Task ClearReactionsAsync(ulong threadId, ulong messageId, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(client: client, channelId: threadId, ct: ct);

        if (await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, Options(ct)) is IUserMessage message)
            await message.RemoveAllReactionsAsync(Options(ct));
    }

    public async Task MarkDeliveredAsync(SylvanMailMessage message, CancellationToken ct)
    {
        try
        {
            var channel = await MessageChannelAsync(client: client, channelId: message.SourceChannelId, ct: ct);

            if (await channel.GetMessageAsync(id: message.SourceMessageId, mode: CacheMode.AllowDownload, Options(ct)) is IUserMessage source)
            {
                await source.RemoveAllReactionsAsync(Options(ct));
                await source.AddReactionAsync(new Emoji("\u2705"), Options(ct));
            }
        }
        catch (Exception exception)
        {
            // The payload was delivered; a failed reaction must never turn it into a failed delivery or cause a resend.
            LogFailure(logger: logger, "success reaction", sourceId: message.SourceMessageId, exception: exception);
        }
    }
}
