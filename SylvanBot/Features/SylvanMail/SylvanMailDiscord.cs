namespace SylvanBot.Features.SylvanMail;

using System.Net;
using Discord.Net;
using Discord.Rest;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailDiscord(DiscordSocketClient client, HttpClient http, ILogger logger) : ISylvanMailDiscord
{
    private const int MaxAttachmentBytes = 8 * 1024 * 1024;

    public async Task ValidateForumAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct)
    {
        var guild = client.GetGuild(guildId);

        if (guild is not { IsConnected: true, HasAllMembers: true })
            throw new BotCommandException("Guild/member data is not ready; SylvanMail refuses to guess private access.");

        var role = guild.GetRole(roleId);

        if (role is null || role.Id == guild.EveryoneRole.Id || role.IsManaged)
            throw new BotCommandException("The configured Senior Moderator role cannot be resolved or is not an assignable staff role.");

        var forum = await GetForumAsync(guildId, forumId, ct);
        OverwritePermissions? everyone = forum.GetPermissionOverwrite(guild.EveryoneRole);

        if (guild.EveryoneRole.Permissions.Administrator || everyone?.ViewChannel != PermValue.Deny)
            throw new BotCommandException(
                "The forum is exposed: explicitly deny View Channel to @everyone. No permissions or settings were changed.");

        OverwritePermissions? staffAccess = forum.GetPermissionOverwrite(role);

        if (!role.Permissions.Administrator && staffAccess?.ViewChannel != PermValue.Allow)
            throw new BotCommandException("Explicitly allow the Senior Moderator role to view this forum before enabling SylvanMail.");

        foreach (var overwrite in forum.PermissionOverwrites)
        {
            if (overwrite.TargetType != PermissionTarget.User || overwrite.Permissions.ViewChannel != PermValue.Allow)
                continue;

            var member = guild.GetUser(overwrite.TargetId);

            var permitted = overwrite.TargetId == client.CurrentUser.Id
                            || member?.GuildPermissions.Administrator == true
                            || member?.Roles.Any(item => item.Id == roleId) == true;

            if (!permitted)
                throw new BotCommandException(
                    "The forum is exposed: a member outside Senior Moderators has a View Channel allow. Remove that access before enabling SylvanMail.");
        }

        var permissions = guild.CurrentUser.GetPermissions(forum);

        var sufficient = permissions is { ViewChannel: true, SendMessages: true } and
        { SendMessagesInThreads: true, ReadMessageHistory: true, AttachFiles: true, ManageThreads: true, EmbedLinks: true };

        if (!sufficient)
            throw new BotCommandException("Sylvan needs View Channel, Send Messages (Create Posts), Send Messages in Threads, Read Message History, Attach Files, Manage Threads and Embed Links in the forum.");

        if (!role.IsMentionable && !permissions.MentionEveryone)
            throw new BotCommandException("Sylvan must be allowed to mention the selected staff role when opening a post (make the role mentionable or grant Mention All Roles here).");

        if (forum.Flags.HasFlag(ChannelFlags.RequireTag) && forum.Tags.Count == 0)
            throw new BotCommandException("The forum requires a tag but has no available tags. No settings changed.");
    }

    public async Task<bool> IsStaffAsync(ulong guildId, ulong authorId, ulong roleId, CancellationToken ct)
    {
        var member = await client.Rest.GetGuildUserAsync(guildId, authorId, Options(ct));

        return member?.IsBot == false && member.RoleIds.Contains(roleId);
    }

    public async Task<ulong?> FindPostAsync(SylvanMailConversation conversation, CancellationToken ct)
    {
        var forum = await GetForumAsync(conversation.GuildId, conversation.ForumId, ct);
        IReadOnlyCollection<IThreadChannel> active = await forum.GetActiveThreadsAsync(Options(ct));
        var found = await FindMatchingPostAsync(active, conversation.Number, ct);

        if (found.HasValue)
            return found;

        DateTimeOffset? before = null;

        // Only read Sylvan's own starter messages, never staff discussion. Titles can be freely renamed.
        while (true)
        {
            IReadOnlyCollection<IThreadChannel> archived = await forum.GetPublicArchivedThreadsAsync(100, before, Options(ct));

            found = await FindMatchingPostAsync(archived, conversation.Number, ct);

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
        var forum = await GetForumAsync(conversation.GuildId, conversation.ForumId, ct);
        ForumTag[] tags = forum.Flags.HasFlag(ChannelFlags.RequireTag) ? [forum.Tags.First()] : [];

        var embed = new EmbedBuilder()
            .WithTitle($"Case #{conversation.Number}")
            .WithDescription("Mention Sylvan explicitly to send a staff reply. Messages without a delivery receipt remain unconfirmed.")
            .WithFooter("Beta · Bugs and lost messages are possible")
            .AddField("User ID", $"```\n{conversation.UserId}\n```", inline: false)
            .AddField("Username", $"```\n{username}\n```", inline: false);

        var member = client.GetGuild(conversation.GuildId)?.GetUser(conversation.UserId);

        if (member is not null)
            embed.AddField("Member", member.Mention, inline: false);

        var post = await AcceptedAsync(() => forum.CreatePostAsync(
            username,
            // Discord notifications require the role mention in message content, alongside the embed.
            text: $"<@&{roleId}>",
            embed: embed.Build(),
            options: Options(ct),
            allowedMentions: new AllowedMentions { RoleIds = [roleId] },
            tags: tags));

        return post.Id;
    }

    public async Task<bool> OpenPostAsync(SylvanMailConversation conversation, CancellationToken ct)
    {
        IChannel? channel;

        try
        {
            channel = await client.Rest.GetChannelAsync(conversation.ThreadId!.Value, Options(ct));
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownChannel || exception.HttpCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        if (channel is not RestThreadChannel thread || thread.GuildId != conversation.GuildId || thread.ParentChannelId != conversation.ForumId)
            return false;

        if (thread.IsLocked)
            throw new BotCommandException("The mapped post is locked. No replacement was created and no message was relayed.");

        if (thread.IsArchived)
        {
            try
            {
                await thread.ModifyAsync(properties => properties.Archived = false, Options(ct));
            }
            catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownChannel || exception.HttpCode == HttpStatusCode.Forbidden)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<ulong> OpenDmAsync(ulong userId, CancellationToken ct)
    {
        var user = await client.Rest.GetUserAsync(userId, Options(ct));

        if (user is null)
            throw new BotCommandException("Discord could not resolve the recipient; no reply was sent.");

        var channel = await AcceptedAsync(() => user.CreateDMChannelAsync(Options(ct)));

        return channel.Id;
    }

    public async Task<ulong> SendTextAsync(ulong channelId, string text, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(channelId, ct);
        var sent = await AcceptedAsync(() => channel.SendMessageAsync(text, options: Options(ct), allowedMentions: AllowedMentions.None));

        return sent.Id;
    }

    public async Task<ulong> SendAttachmentAsync(ulong channelId, SylvanMailAttachment attachment, byte[] bytes, string label, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(channelId, ct);
        await using var stream = new MemoryStream(bytes, writable: false);
        using var file = new FileAttachment(stream, Path.GetFileName(attachment.Filename), isSpoiler: attachment.Spoiler);

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
        var validUrl = Uri.TryCreate(attachment.Url, UriKind.Absolute, out var uri)
                       && string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                       && (string.Equals(uri.Host, "cdn.discordapp.com", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(uri.Host, "media.discordapp.net", StringComparison.OrdinalIgnoreCase));

        if (!validUrl)
            throw new BotCommandException("Not relayed: an attachment does not use a supported Discord attachment host.");

        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxAttachmentBytes)
                throw new BotCommandException("Not relayed: an attachment is unavailable or exceeds 8 MiB.");

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = new MemoryStream();
            var buffer = new byte[81920];
            var count = await source.ReadAsync(buffer, ct);

            while (count > 0)
            {
                if (destination.Length + count > MaxAttachmentBytes)
                    throw new BotCommandException("Not relayed: an attachment exceeds 8 MiB.");

                await destination.WriteAsync(buffer.AsMemory(0, count), ct);
                count = await source.ReadAsync(buffer, ct);
            }

            return destination.Length != attachment.Size
                ? throw new BotCommandException("Not relayed: an attachment download was incomplete or its size changed.")
                : destination.ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new BotCommandException("Not relayed: an attachment download failed.", ex);
        }
    }

    public async Task<ulong?> ReportAsync(
        SylvanMailMessage source,
        ulong? threadId,
        SylvanMailOperation? operation,
        string detail,
        CancellationToken ct)
    {
        var toStaff = source.GuildId is null;
        var failed = operation is null || operation.State is "Uncertain" or "Partial" or "Refused" or "Unpublished";
        var receiptId = operation?.ReceiptMessageId;

        if (threadId.HasValue)
        {
            try
            {
                var channel = await MessageChannelAsync(threadId.Value, ct);
                var report = $"SylvanMail {(toStaff ? "inbound" : "reply")} · source `{source.SourceMessageId}`: {detail}";

                if (receiptId.HasValue)
                {
                    await channel.ModifyMessageAsync(
                        receiptId.Value,
                        properties =>
                        {
                            properties.Content = report;
                            properties.AllowedMentions = AllowedMentions.None;
                        },
                        Options(ct));
                }
                else
                {
                    receiptId = await SendTextAsync(threadId.Value, report, ct);
                }
            }
            catch (Exception exception)
            {
                LogFailure("receipt", source.SourceMessageId, exception);
            }
        }

        if (!toStaff || !failed)
            return receiptId;

        try
        {
            await SendTextAsync(source.SourceChannelId, detail, ct);
        }
        catch (Exception exception)
        {
            LogFailure("user failure notice", source.SourceMessageId, exception);
        }

        return receiptId;
    }

    public async Task<ulong?> AcknowledgeAsync(SylvanMailMessage source, bool first, CancellationToken ct)
    {
        try
        {
            var channel = await MessageChannelAsync(source.SourceChannelId, ct);
            var text = first
                ? "Delivered to staff. Please be patient, someone will get to you soon."
                : "Delivered.";

            var reference = new MessageReference(source.SourceMessageId, source.SourceChannelId, failIfNotExists: false);
            var sent = await AcceptedAsync(() => channel.SendMessageAsync(text,
                options: Options(ct), allowedMentions: AllowedMentions.None, messageReference: reference));

            return sent.Id;
        }
        catch (Exception exception)
        {
            LogFailure("acknowledgement", source.SourceMessageId, exception);
            return null;
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
            var starter = await post.GetMessageAsync(post.Id, options: Options(ct));

            if (starter?.Author.Id != client.CurrentUser.Id)
                continue;

            var matchesEmbed = starter.Embeds
                .Any(embed => string.Equals(embed.Title, caseReference, StringComparison.Ordinal));

            var matches = matchesEmbed || starter.Content.EndsWith($"\n{caseReference}", StringComparison.Ordinal);

            if (matches)
                return post.Id;
        }

        return null;
    }

    private static RequestOptions Options(CancellationToken ct) => new()
    {
        CancelToken = ct,
        RetryMode = RetryMode.AlwaysFail,
    };

    private static async Task<T> AcceptedAsync<T>(Func<Task<T>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpException exception) when ((int)exception.HttpCode is >= 400 and < 500 && exception.HttpCode != HttpStatusCode.TooManyRequests)
        {
            throw new BotCommandException($"Discord refused the operation (HTTP {(int)exception.HttpCode}); no automatic retry.", exception);
        }
    }

    private async Task<IForumChannel> GetForumAsync(ulong guildId, ulong forumId, CancellationToken ct)
    {
        var channel = await client.Rest.GetChannelAsync(forumId, Options(ct));

        if (channel is not IForumChannel forum || forum.GuildId != guildId || channel is IMediaChannel)
            throw new BotCommandException("The configured target is not an accessible forum in this guild.");

        return forum;
    }

    private async Task<IMessageChannel> MessageChannelAsync(ulong channelId, CancellationToken ct)
    {
        var channel = await client.GetChannelAsync(channelId, options: Options(ct));

        return channel as IMessageChannel ?? throw new BotCommandException("The delivery channel is inaccessible.");
    }

    private void LogFailure(string stage, ulong sourceId, Exception exception) =>
        logger.Warning("SylvanMail {Stage} failed for source {SourceId}: {ExceptionType}", stage, sourceId, exception.GetType().Name);
}