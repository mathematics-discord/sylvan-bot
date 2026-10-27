using System.Globalization;
using System.Net;
using Discord.Net;
using Discord.Rest;
using SylvanBot.Application.Features.SylvanMail;

namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailDiscord(DiscordSocketClient client, HttpClient http, ILogger logger) : ISylvanMailDiscord
{
    public const string SendEmoji = "\U0001F4E4";

    private const int MaxAttachmentBytes = 8 * 1024 * 1024;

    public static SylvanMailMessage ReadMessage(IUserMessage source, ulong? guildId)
    {
        ImmutableArray<SylvanMailAttachment> attachments = source.Attachments
           .Select(item => new SylvanMailAttachment(Url: item.Url, Filename: item.Filename, Size: item.Size, item.IsSpoiler()))
           .ToImmutableArray();

        var unsupported = source.Stickers.Count > 0
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

    public async Task ValidateForumAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct, bool validatePing = true)
    {
        var guild = client.GetGuild(guildId);

        if (guild is not { IsConnected: true, HasAllMembers: true })
            throw new BotCommandException("Guild/member data is not ready; SylvanMail refuses to guess private access.");

        var role = guild.GetRole(roleId);

        if (validatePing && role is null)
            throw new BotCommandException("The configured ping role cannot be resolved.");

        var                   forum    = await GetForumAsync(guildId: guildId, forumId: forumId, ct: ct);
        OverwritePermissions? everyone = forum.GetPermissionOverwrite(guild.EveryoneRole);

        if (guild.EveryoneRole.Permissions.Administrator || everyone?.ViewChannel != PermValue.Deny)
            throw new BotCommandException("The forum is exposed: explicitly deny View Channel to @everyone. No permissions or settings were changed.");

        var permissions = guild.CurrentUser.GetPermissions(forum);

        var sufficient = permissions is
            {
                ViewChannel: true,
                SendMessages: true,
            }
            and
            {
                SendMessagesInThreads: true,
                ReadMessageHistory: true,
                AttachFiles: true,
                ManageThreads: true,
                EmbedLinks: true,
                AddReactions: true,
            };

        if (!sufficient)
            throw new BotCommandException("Sylvan needs View Channel, Send Messages (Create Posts), Send Messages in Threads, Read Message History, Attach Files, Manage Threads, Embed Links and Add Reactions in the forum.");

        var cannotPing = role is not null
            && (role.Id == guild.EveryoneRole.Id || !role.IsMentionable)
            && !permissions.MentionEveryone;

        if (validatePing && cannotPing)
            throw new BotCommandException("Sylvan must be allowed to mention the selected ping role (make the role mentionable or grant Mention All Roles here).");

        if (forum.Flags.HasFlag(ChannelFlags.RequireTag) && forum.Tags.Count == 0)
            throw new BotCommandException("The forum requires a tag but has no available tags. No settings changed.");
    }

    public async Task<SylvanMailMessage?> ReadDraftAsync(ulong threadId, ulong messageId, ulong authorId, bool confirm, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(channelId: threadId, ct: ct);

        if (await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, Options(ct)) is not IUserMessage source
            || source.Author.Id != authorId
            || source.Author.IsBot
            || source.Author.IsWebhook
            || source.Type is not (MessageType.Default or MessageType.Reply)
            || channel is not IThreadChannel thread)
        {
            return null;
        }

        var hasMarker = source.Reactions.TryGetValue(new Emoji(SendEmoji), out var reaction) && reaction.IsMe;

        if (confirm && !hasMarker)
            return null;

        var content      = source.Content ?? string.Empty;
        var reply        = SylvanMailReply.Parse(content: content, botId: client.CurrentUser.Id, replyToBot: false, replyPingEnabled: false);
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

            return ReadMessage(source: source, guildId: thread.GuildId);
        }

        var prefix = reply.Anonymous ? "From staff: " : $"From {Format.Sanitize(source.Author.Username)}: ";

        return ReadMessage(source: source, guildId: thread.GuildId) with { Content = reply.Content, OutboundPrefix = prefix };
    }

    public async Task SetDraftReadyAsync(ulong threadId, ulong messageId, bool ready, CancellationToken ct)
    {
        try
        {
            var channel = await MessageChannelAsync(channelId: threadId, ct: ct);

            if (await channel.GetMessageAsync(id: messageId, mode: CacheMode.AllowDownload, Options(ct)) is not IUserMessage message)
                return;

            var emoji  = new Emoji(SendEmoji);
            var marked = message.Reactions.TryGetValue(key: emoji, out var reaction) && reaction.IsMe;

            if (ready == marked)
                return;

            if (ready)
                await message.AddReactionAsync(emote: emoji, Options(ct));
            else
                await message.RemoveReactionAsync(emote: emoji, userId: client.CurrentUser.Id, Options(ct));
        }
        catch (Exception exception)
        {
            LogFailure("draft reaction", sourceId: messageId, exception: exception);
        }
    }

    public async Task MarkDeliveredAsync(SylvanMailMessage message, CancellationToken ct)
    {
        try
        {
            var channel = await MessageChannelAsync(channelId: message.SourceChannelId, ct: ct);

            if (await channel.GetMessageAsync(id: message.SourceMessageId, mode: CacheMode.AllowDownload, Options(ct)) is IUserMessage source)
                await source.AddReactionAsync(new Emoji("\u2611\uFE0F"), Options(ct));

            await SetDraftReadyAsync(threadId: message.SourceChannelId, messageId: message.SourceMessageId, ready: false, ct: ct);
        }
        catch (Exception exception)
        {
            // The payload was delivered; a failed reaction must never turn it into a failed delivery or cause a resend.
            LogFailure("success reaction", sourceId: message.SourceMessageId, exception: exception);
        }
    }

    public async Task<string> InboundPrefixAsync(ulong guildId, SylvanMailMessage message, CancellationToken ct)
    {
        IGuildUser? member;

        try
        {
            member = await client.Rest.GetGuildUserAsync(guildId: guildId, id: message.AuthorId, Options(ct));
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMember)
        {
            member = null;
        }

        var name      = member is null ? Format.Sanitize(message.Username) : $"<@{message.AuthorId}>";
        var timestamp = message.Timestamp.UtcDateTime.ToString("HH:mm:ss", provider: CultureInfo.InvariantCulture);

        return $"[{timestamp}] {name}: ";
    }

    public async Task PingAfterGapAsync(SylvanMailSettings settings, ulong threadId, SylvanMailMessage message, CancellationToken ct)
    {
        try
        {
            var dm     = await MessageChannelAsync(channelId: message.SourceChannelId, ct: ct);
            var before = message.SourceMessageId;

            while (true)
            {
                IEnumerable<IMessage> page = await dm.GetMessagesAsync(fromMessageId: before, dir: Direction.Before, limit: 100, mode: CacheMode.AllowDownload, Options(ct)).FlattenAsync();

                IMessage? previous = null;
                var       oldest   = before;

                foreach (var item in page)
                {
                    oldest = Math.Min(val1: oldest, val2: item.Id);

                    var isNewerInbound = item.Author.Id == message.AuthorId
                        && item.Source == MessageSource.User
                        && (previous is null || item.Id > previous.Id);

                    if (isNewerInbound)
                        previous = item;
                }

                if (previous is not null)
                {
                    if (message.Timestamp - previous.Timestamp < TimeSpan.FromHours(3))
                        return;

                    var thread   = await MessageChannelAsync(channelId: threadId, ct: ct);
                    var everyone = settings.RoleId == settings.GuildId;

                    var mentions = everyone
                        ? new AllowedMentions(AllowedMentionTypes.Everyone)
                        : new AllowedMentions { RoleIds = [settings.RoleId] };
                    var text = everyone ? "@everyone" : $"<@&{settings.RoleId}>";
                    await AcceptedAsync(() => thread.SendMessageAsync(text: text, options: Options(ct), allowedMentions: mentions));

                    return;
                }

                if (oldest == before)
                    return;

                before = oldest;
            }
        }
        catch (Exception exception)
        {
            // A ping failure is separate from delivery of the user's message. Never resend the payload for it.
            LogFailure("inbound gap ping", sourceId: message.SourceMessageId, exception: exception);
        }
    }

    public async Task<ulong?> FindPostAsync(SylvanMailConversation conversation, CancellationToken ct)
    {
        var                                 forum  = await GetForumAsync(guildId: conversation.GuildId, forumId: conversation.ForumId, ct: ct);
        IReadOnlyCollection<IThreadChannel> active = await forum.GetActiveThreadsAsync(Options(ct));
        var                                 found  = await FindMatchingPostAsync(posts: active, number: conversation.Number, ct: ct);

        if (found.HasValue)
            return found;

        DateTimeOffset? before = null;

        // Only read Sylvan's own starter messages, never staff discussion. Titles can be freely renamed.
        while (true)
        {
            IReadOnlyCollection<IThreadChannel> archived = await forum.GetPublicArchivedThreadsAsync(limit: 100, before: before, Options(ct));

            found = await FindMatchingPostAsync(posts: archived, number: conversation.Number, ct: ct);

            if (found.HasValue)
                return found;

            if (archived.Count < 100)
                break;

            var oldest = archived
               .Min(item => item.ArchiveTimestamp);

            if (before.HasValue && oldest >= before.Value)
                throw new BotCommandException("Post reconciliation could not finish safely; no new post was created.");

            before = oldest;
        }

        return null;
    }

    public async Task<ulong> CreatePostAsync(SylvanMailConversation conversation, ulong roleId, string username, CancellationToken ct)
    {
        var        forum = await GetForumAsync(guildId: conversation.GuildId, forumId: conversation.ForumId, ct: ct);
        ForumTag[] tags  = forum.Flags.HasFlag(ChannelFlags.RequireTag) ? [forum.Tags.First()] : [];

        var embed = new EmbedBuilder()
           .WithTitle($"Case #{conversation.Number}")
           .WithDescription("Mention Sylvan for an anonymous draft, or reply to Sylvan with the reply ping enabled to sign it. Only the author can click \U0001F4E4 to send the latest draft. \u2611\uFE0F confirms delivery; errors appear here.")
           .WithFooter("Beta · Bugs and lost messages are possible")
           .AddField("User ID", $"```\n{conversation.UserId}\n```", inline: false)
           .AddField("Username", $"```\n{username}\n```", inline: false);

        var member = client.GetGuild(conversation.GuildId)?.GetUser(conversation.UserId);

        if (member is not null)
            embed.AddField("Member", value: member.Mention, inline: false);

        var post = await AcceptedAsync(() => forum.CreatePostAsync(
                                           title: username,
                                           // Discord notifications require the role mention in message content, alongside the embed.
                                           text: roleId == conversation.GuildId ? "@everyone" : $"<@&{roleId}>",
                                           embed: embed.Build(),
                                           options: Options(ct),
                                           allowedMentions: roleId == conversation.GuildId
                                               ? new AllowedMentions(AllowedMentionTypes.Everyone)
                                               : new AllowedMentions { RoleIds = [roleId] },
                                           tags: tags));

        return post.Id;
    }

    public async Task<bool> OpenPostAsync(SylvanMailConversation conversation, CancellationToken ct)
    {
        IChannel? channel;

        try
        {
            channel = await client.Rest.GetChannelAsync(id: conversation.ThreadId!.Value, Options(ct));
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownChannel || exception.HttpCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        if (channel is not RestThreadChannel thread || thread.GuildId != conversation.GuildId || thread.ParentChannelId != conversation.ForumId)
            return false;

        if (thread.IsLocked)
            throw new BotCommandException("The mapped post is locked. No replacement was created and no message was relayed.");

        if (!thread.IsArchived)
            return true;

        try
        {
            await thread.ModifyAsync(properties => properties.Archived = false, Options(ct));
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownChannel || exception.HttpCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        return true;
    }

    public async Task<ulong> OpenDmAsync(ulong userId, CancellationToken ct)
    {
        var user = await client.Rest.GetUserAsync(id: userId, Options(ct));

        if (user is null)
            throw new BotCommandException("Discord could not resolve the recipient; no reply was sent.");

        var channel = await AcceptedAsync(() => user.CreateDMChannelAsync(Options(ct)));

        return channel.Id;
    }

    public async Task<ulong> SendTextAsync(ulong channelId, string text, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(channelId: channelId, ct: ct);
        var sent    = await AcceptedAsync(() => channel.SendMessageAsync(text: text, options: Options(ct), allowedMentions: AllowedMentions.None));

        return sent.Id;
    }

    public async Task<ulong> SendAttachmentAsync(ulong channelId, SylvanMailAttachment attachment, byte[] bytes, string label, CancellationToken ct)
    {
        var             channel = await MessageChannelAsync(channelId: channelId, ct: ct);
        await using var stream  = new MemoryStream(buffer: bytes, writable: false);
        using var       file    = new FileAttachment(stream: stream, Path.GetFileName(attachment.Filename), isSpoiler: attachment.Spoiler);

        var sent = await AcceptedAsync(() => channel.SendFilesAsync(
                                           [file],
                                           text: label,
                                           options: Options(ct),
                                           allowedMentions: AllowedMentions.None));

        return sent.Id;
    }

    public async Task<byte[]> DownloadAsync(SylvanMailAttachment attachment, CancellationToken ct)
    {
        // Only Discord attachment hosts. Never log signed URLs, HTTP exception messages or file content.
        var validUrl = Uri.TryCreate(uriString: attachment.Url, uriKind: UriKind.Absolute, out var uri)
            && string.Equals(a: uri.Scheme, "https", comparisonType: StringComparison.OrdinalIgnoreCase)
            && (string.Equals(a: uri.Host, "cdn.discordapp.com", comparisonType: StringComparison.OrdinalIgnoreCase)
                || string.Equals(a: uri.Host, "media.discordapp.net", comparisonType: StringComparison.OrdinalIgnoreCase));

        if (!validUrl)
            throw new BotCommandException("Not relayed: an attachment does not use a supported Discord attachment host.");

        try
        {
            using var response = await http.GetAsync(requestUri: uri, completionOption: HttpCompletionOption.ResponseHeadersRead, cancellationToken: ct);

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxAttachmentBytes)
                throw new BotCommandException("Not relayed: an attachment is unavailable or exceeds 8 MiB.");

            await using var source      = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = new MemoryStream();
            var             buffer      = new byte[81920];
            var             count       = await source.ReadAsync(buffer: buffer, cancellationToken: ct);

            while (count > 0)
            {
                if (destination.Length + count > MaxAttachmentBytes)
                    throw new BotCommandException("Not relayed: an attachment exceeds 8 MiB.");

                await destination.WriteAsync(buffer.AsMemory(start: 0, length: count), cancellationToken: ct);
                count = await source.ReadAsync(buffer: buffer, cancellationToken: ct);
            }

            return destination.Length != attachment.Size
                ? throw new BotCommandException("Not relayed: an attachment download was incomplete or its size changed.")
                : destination.ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new BotCommandException("Not relayed: an attachment download failed.", innerException: ex);
        }
    }

    public async Task<ulong?> ReportAsync(
        SylvanMailMessage source,
        ulong? threadId,
        SylvanMailOperation? operation,
        string detail,
        CancellationToken ct)
    {
        var toStaff   = source.GuildId is null;
        var failed    = operation is null || operation.State is "Uncertain" or "Partial" or "Refused" or "Unpublished";
        var receiptId = operation?.ReceiptMessageId;

        if (toStaff && failed)
        {
            try
            {
                const string guidance = "If the error describes a problem with your text or attachments, fix it and send a new DM. "
                    + "If SylvanMail is busy, try again later. For partial or uncertain delivery, or any problem you cannot fix yourself, "
                    + "DM <@203864799870844928> directly or contact another member of staff. Ask them to check what arrived before resending.";

                await SendTextAsync(channelId: source.SourceChannelId, $"{detail}\n\n{guidance}", ct: ct);
            }
            catch (Exception exception)
            {
                LogFailure("user failure notice", sourceId: source.SourceMessageId, exception: exception);
            }
        }

        if (!threadId.HasValue || !failed)
            return receiptId;

        try
        {
            var channel = await MessageChannelAsync(channelId: threadId.Value, ct: ct);
            var report  = $"Delivery error · source `{source.SourceMessageId}`: {detail}";

            if (receiptId.HasValue)
            {
                await channel.ModifyMessageAsync(
                    messageId: receiptId.Value,
                    properties =>
                    {
                        properties.Content         = report;
                        properties.AllowedMentions = AllowedMentions.None;
                    },
                    Options(ct));
            }
            else
            {
                var reference = toStaff ? null : new MessageReference(messageId: source.SourceMessageId, channelId: source.SourceChannelId, failIfNotExists: false);

                var sent = await AcceptedAsync(() => channel.SendMessageAsync(
                                                   text: report,
                                                   options: Options(ct),
                                                   allowedMentions: AllowedMentions.None,
                                                   messageReference: reference));
                receiptId = sent.Id;
            }
        }
        catch (Exception exception)
        {
            LogFailure("receipt", sourceId: source.SourceMessageId, exception: exception);
        }

        return receiptId;
    }

    public async Task<ulong?> AcknowledgeAsync(SylvanMailMessage source, bool first, CancellationToken ct)
    {
        try
        {
            var channel = await MessageChannelAsync(channelId: source.SourceChannelId, ct: ct);

            var text = first
                ? "Delivered to staff. Please be patient, someone will get to you soon."
                : "Delivered.";

            var reference = new MessageReference(messageId: source.SourceMessageId, channelId: source.SourceChannelId, failIfNotExists: false);

            var sent = await AcceptedAsync(() => channel.SendMessageAsync(
                                               text: text,
                                               options: Options(ct),
                                               allowedMentions: AllowedMentions.None,
                                               messageReference: reference));

            return sent.Id;
        }
        catch (Exception exception)
        {
            LogFailure("acknowledgement", sourceId: source.SourceMessageId, exception: exception);

            return null;
        }
    }

    private static RequestOptions Options(CancellationToken ct) => new()
    {
        CancelToken = ct,
        RetryMode   = RetryMode.AlwaysFail,
    };

    private static async Task<T> AcceptedAsync<T>(Func<Task<T>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpException exception) when ((int)exception.HttpCode is >= 400 and < 500 && exception.HttpCode != HttpStatusCode.TooManyRequests)
        {
            throw new BotCommandException($"Discord refused the operation (HTTP {(int)exception.HttpCode}); no automatic retry.", innerException: exception);
        }
    }

    private async Task<ulong?> FindMatchingPostAsync(IEnumerable<IThreadChannel> posts, long number, CancellationToken ct)
    {
        IEnumerable<IThreadChannel> ownPosts = posts
           .Where(post => post.OwnerId == client.CurrentUser.Id);

        var caseReference = $"Case #{number}";

        foreach (var post in ownPosts)
        {
            // Forum posts and their starter messages have the same ID.
            var starter = await post.GetMessageAsync(id: post.Id, options: Options(ct));

            if (starter?.Author.Id != client.CurrentUser.Id)
                continue;

            var matchesEmbed = starter.Embeds
               .Any(embed => string.Equals(a: embed.Title, b: caseReference, comparisonType: StringComparison.Ordinal));

            var matches = matchesEmbed || starter.Content.EndsWith($"\n{caseReference}", comparisonType: StringComparison.Ordinal);

            if (matches)
                return post.Id;
        }

        return null;
    }

    private async Task<IForumChannel> GetForumAsync(ulong guildId, ulong forumId, CancellationToken ct)
    {
        var channel = await client.Rest.GetChannelAsync(id: forumId, Options(ct));

        if (channel is not IForumChannel forum || forum.GuildId != guildId || channel is IMediaChannel)
            throw new BotCommandException("The configured target is not an accessible forum in this guild.");

        return forum;
    }

    private async Task<IMessageChannel> MessageChannelAsync(ulong channelId, CancellationToken ct)
    {
        var channel = await client.Rest.GetChannelAsync(id: channelId, Options(ct));

        return channel as IMessageChannel ?? throw new BotCommandException("The delivery channel is inaccessible.");
    }

    private void LogFailure(string stage, ulong sourceId, Exception exception) =>
        logger.Warning("SylvanMail {Stage} failed for source {SourceId}: {ExceptionType}", propertyValue0: stage, propertyValue1: sourceId, propertyValue2: exception.GetType().Name);
}