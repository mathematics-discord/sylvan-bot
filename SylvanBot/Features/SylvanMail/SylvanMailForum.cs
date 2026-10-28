using System.Net;
using Discord.Net;
using Discord.Rest;
using SylvanBot.Application.Features.SylvanMail;
using static SylvanBot.Features.SylvanMail.SylvanMailRequests;

namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailForum(DiscordSocketClient client) : ISylvanMailForum
{
    public async Task ValidateForumAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct, bool validatePing = true)
    {
        var guild = client.GetGuild(guildId);

        if (guild is not { IsConnected: true, HasAllMembers: true })
            throw new BotCommandException("Guild/member data is not ready; SylvanMail refuses to guess private access.");

        var role = guild.GetRole(roleId);

        if (validatePing && role is null)
            throw new BotCommandException("The configured ping role cannot be resolved.");

        var forum = await GetForumAsync(guildId: guildId, forumId: forumId, ct: ct);
        OverwritePermissions? everyone = forum.GetPermissionOverwrite(guild.EveryoneRole);

        if (guild.EveryoneRole.Permissions.Administrator || everyone?.ViewChannel != PermValue.Deny)
            throw new BotCommandException(
                "The forum is exposed: explicitly deny View Channel to @everyone. No permissions or settings were changed.");

        var permissions = guild.CurrentUser.GetPermissions(forum);

        var sufficient =
            permissions
                is { ViewChannel: true, SendMessages: true }
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
            throw new BotCommandException(
                "Sylvan needs View Channel, Send Messages (Create Posts), Send Messages in Threads, Read Message History, Attach Files, Manage Threads, Embed Links and Add Reactions in the forum.");

        var cannotPing = role is not null && (role.Id == guild.EveryoneRole.Id || !role.IsMentionable) && !permissions.MentionEveryone;

        if (validatePing && cannotPing)
            throw new BotCommandException(
                "Sylvan must be allowed to mention the selected ping role (make the role mentionable or grant Mention All Roles here).");

        if (forum.Flags.HasFlag(ChannelFlags.RequireTag) && forum.Tags.Count == 0)
            throw new BotCommandException("The forum requires a tag but has no available tags. No settings changed.");
    }

    public async Task<Option<ulong>> FindPostAsync(ulong guildId, ulong forumId, long number, CancellationToken ct)
    {
        var forum = await GetForumAsync(guildId: guildId, forumId: forumId, ct: ct);
        IReadOnlyCollection<IThreadChannel> active = await forum.GetActiveThreadsAsync(Options(ct));
        var found = await FindMatchingPostAsync(posts: active, number: number, ct: ct);

        if (found.HasValue)
            return new Some<ulong>(found.Value);

        DateTimeOffset? before = null;

        // Only read Sylvan's own starter messages, never staff discussion. Titles can be freely renamed.
        while (true)
        {
            IReadOnlyCollection<IThreadChannel> archived = await forum.GetPublicArchivedThreadsAsync(limit: 100, before: before, Options(ct));

            found = await FindMatchingPostAsync(posts: archived, number: number, ct: ct);

            if (found.HasValue)
                return new Some<ulong>(found.Value);

            if (archived.Count < 100)
                break;

            var oldest = archived.Min(item => item.ArchiveTimestamp);

            if (before.HasValue && oldest >= before.Value)
                throw new BotCommandException("Post reconciliation could not finish safely; no new post was created.");

            before = oldest;
        }

        return new None();
    }

    public async Task<ulong> CreatePostAsync(SylvanMailPost post, ulong roleId, string username, CancellationToken ct)
    {
        var forum = await GetForumAsync(guildId: post.GuildId, forumId: post.ForumId, ct: ct);
        ForumTag[] tags = forum.Flags.HasFlag(ChannelFlags.RequireTag) ? [forum.Tags.First()] : [];

        var embed = new EmbedBuilder()
            .WithTitle($"Case #{post.Number}")
            .WithDescription(
                "Mention Sylvan for an anonymous draft, or reply to Sylvan with the reply ping enabled to sign it. Only the author can click \U0001F4E4 to send the latest draft. \u2611\uFE0F confirms delivery; errors appear here.")
            .WithFooter("Beta · Bugs and lost messages are possible")
            .AddField("User ID", $"```\n{post.UserId}\n```", inline: false)
            .AddField("Username", $"```\n{username}\n```", inline: false);

        var member = client.GetGuild(post.GuildId)?.GetUser(post.UserId);

        if (member is not null)
            embed.AddField("Member", value: member.Mention, inline: false);

        var created = await AcceptedAsync(() =>
            forum.CreatePostAsync(
                title: username,
                // Discord notifications require the role mention in message content, alongside the embed.
                text: roleId == post.GuildId ? "@everyone" : $"<@&{roleId}>",
                embed: embed.Build(),
                options: Options(ct),
                allowedMentions: roleId == post.GuildId
                    ? new AllowedMentions(AllowedMentionTypes.Everyone)
                    : new AllowedMentions { RoleIds = [roleId] },
                tags: tags));

        return created.Id;
    }

    public async Task<bool> OpenPostAsync(ulong guildId, ulong forumId, ulong threadId, CancellationToken ct)
    {
        IChannel? channel;

        try
        {
            channel = await client.Rest.GetChannelAsync(id: threadId, Options(ct));
        }
        catch (HttpException exception)
            when (exception.DiscordCode == DiscordErrorCode.UnknownChannel || exception.HttpCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        if (channel is not RestThreadChannel thread || thread.GuildId != guildId || thread.ParentChannelId != forumId)
            return false;

        if (thread.IsLocked)
            throw new BotCommandException("The mapped post is locked. No replacement was created and no message was relayed.");

        if (!thread.IsArchived)
            return true;

        try
        {
            await thread.ModifyAsync(properties => properties.Archived = false, Options(ct));
        }
        catch (HttpException exception)
            when (exception.DiscordCode == DiscordErrorCode.UnknownChannel || exception.HttpCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        return true;
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
}