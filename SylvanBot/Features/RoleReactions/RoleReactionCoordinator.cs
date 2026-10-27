using System.Collections.Concurrent;
using System.Globalization;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RoleReactions;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.RoleReactions;

public sealed class RoleReactionCoordinator(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contextFactory,
    ILogger logger)
{
    private const GuildPermission StaffPermissions = GuildPermission.Administrator
        | GuildPermission.ViewAuditLog
        | GuildPermission.ManageGuild
        | GuildPermission.ManageRoles
        | GuildPermission.ManageChannels
        | GuildPermission.ManageMessages
        | GuildPermission.ManageThreads
        | GuildPermission.ManageWebhooks
        | GuildPermission.ManageNicknames
        | GuildPermission.ManageEmojisAndStickers
        | GuildPermission.ManageEvents
        | GuildPermission.KickMembers
        | GuildPermission.BanMembers
        | GuildPermission.ModerateMembers
        | GuildPermission.MuteMembers
        | GuildPermission.DeafenMembers
        | GuildPermission.MoveMembers
        | GuildPermission.PinMessages
        | GuildPermission.BypassSlowmode
        | GuildPermission.MentionEveryone
        | GuildPermission.ViewGuildInsights
        | GuildPermission.ViewMonetizationAnalytics;

    // Configuration and role changes use the same guild gate, including pending reaction events.
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _guildGates = new();

    public async Task<string> AddAsync(
        SocketGuild guild,
        SocketGuildUser moderator,
        string messageLink,
        ulong roleId,
        string emoji,
        CancellationToken ct)
    {
        (ulong ChannelId, ulong MessageId)? link = ParseMessageLink(messageLink, guild.Id);

        if (link is null)
            return "Provide a Discord message link from this server, not a message ID.";

        var emote = ParseEmoji(emoji);

        if (emote is null)
            return "Provide one Unicode emoji or a custom emoji mention.";

        var gate = _guildGates.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            var role      = guild.GetRole(roleId);
            var roleError = ValidateRole(guild, role);

            if (roleError is not null)
                return roleError;

            if (moderator.Id != guild.OwnerId && moderator.Hierarchy <= role!.Position)
                return "The target role must be below your highest role.";

            var request = new RequestOptions { CancelToken = ct };
            var channel = await ReadChannelAsync(guild.Id, link.Value.ChannelId, request);

            if (channel is not IGuildChannel guildChannel)
                return "The linked channel is unavailable or does not belong to this server.";

            var moderatorPermissions = moderator.GetPermissions(guildChannel);
            var botPermissions       = guild.CurrentUser.GetPermissions(guildChannel);

            if (!moderatorPermissions.ViewChannel || !moderatorPermissions.ReadMessageHistory)
                return "You must be able to view the linked channel and read its message history.";

            if (!botPermissions.ViewChannel || !botPermissions.ReadMessageHistory || !botPermissions.AddReactions)
                return "The bot needs View Channel, Read Message History, and Add Reactions in the linked channel.";

            var message = await channel.GetMessageAsync(link.Value.MessageId, options: request);

            if (message is null)
                return "The linked message could not be found.";

            return await ConfigureReactionAsync(guild.Id, message, roleId, emote, request, ct);
        }
        catch (HttpException exception)
        {
            logger.Warning(exception, "Could not read role reaction target in guild {GuildId} on message {MessageId}", guild.Id, link.Value.MessageId);

            return "The bot could not read the linked message. Check that it still exists and the bot can access its channel or thread.";
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> RemoveAsync(SocketGuild guild, string messageLink, string emoji, CancellationToken ct)
    {
        (ulong ChannelId, ulong MessageId)? link = ParseMessageLink(messageLink, guild.Id);

        if (link is null)
            return "Provide a Discord message link from this server, not a message ID.";

        var emote = ParseEmoji(emoji);

        if (emote is null)
            return "Provide one Unicode emoji or a custom emoji mention.";

        var gate = _guildGates.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            var             emojiKey = GetEmojiKey(emote);
            await using var db       = await contextFactory.CreateDbContextAsync(ct);

            var mapping = await db.RoleReactions
               .SingleOrDefaultAsync(
                    mapping => mapping.GuildId == guild.Id
                        && mapping.ChannelId == link.Value.ChannelId
                        && mapping.MessageId == link.Value.MessageId
                        && mapping.EmojiKey == emojiKey,
                    ct);

            if (mapping is null)
                return "No role reaction is configured for that message and emoji.";

            db.RoleReactions.Remove(mapping);
            await db.SaveChangesAsync(ct);

            try
            {
                var request         = new RequestOptions { CancelToken = ct };
                var channel         = await ReadChannelAsync(guild.Id, mapping.ChannelId, request);
                var message         = channel is null ? null : await channel.GetMessageAsync(mapping.MessageId, options: request);
                var configuredEmote = ParseEmoji(mapping.EmojiText) ?? emote;

                if (message is not null)
                    await message.RemoveAllReactionsForEmoteAsync(configuredEmote, request);
            }
            catch (HttpException exception) when (exception.HttpCode == System.Net.HttpStatusCode.NotFound)
            {
                // A deleted message/channel needs no visible cleanup; the mapping is already disabled.
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.Warning(exception, "Role reaction disabled but cleanup failed in guild {GuildId} on message {MessageId}", guild.Id, mapping.MessageId);

                return "The mapping is removed and existing roles are unchanged, but the bot could not clear the emoji reactions. Clear them manually; the bot needs Manage Messages for cleanup.";
            }

            return "Removed the role reaction. Existing role memberships are unchanged.";
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ProcessReactionAsync(
        ulong guildId,
        ulong channelId,
        ulong messageId,
        ulong userId,
        IEmote emote,
        bool added,
        CancellationToken ct)
    {
        var gate = _guildGates.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            var             emojiKey = GetEmojiKey(emote);
            await using var db       = await contextFactory.CreateDbContextAsync(ct);

            var mapping = await db.RoleReactions
               .AsNoTracking()
               .SingleOrDefaultAsync(
                    mapping => mapping.GuildId == guildId
                        && mapping.ChannelId == channelId
                        && mapping.MessageId == messageId
                        && mapping.EmojiKey == emojiKey,
                    ct);

            if (mapping is null)
                return;

            var guild = client.GetGuild(guildId);

            if (guild is null)
                return;

            var request = new RequestOptions
            {
                CancelToken    = ct,
                AuditLogReason = $"Role reaction on message {messageId}",
            };

            var member = guild.GetUser(userId) ?? (IGuildUser?)await client.Rest.GetGuildUserAsync(guildId, userId, request);

            if (member?.IsBot != false)
                return;

            await UpdateRoleAsync(guild, member, mapping, added, request);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string GetEmojiKey(IEmote emote)
    {
        if (emote is Emote custom)
            return "custom:" + custom.Id.ToString(CultureInfo.InvariantCulture);

        var unicode = emote.Name
           .Replace("\uFE0E", string.Empty, StringComparison.Ordinal)
           .Replace("\uFE0F", string.Empty, StringComparison.Ordinal);

        return "unicode:" + unicode;
    }

    private static IEmote? ParseEmoji(string text)
    {
        var input = text.Trim();

        if (input.Length is 0 or > 128)
            return null;

        if (Emote.TryParse(input, out var custom))
        {
            var validCustomEmoji = custom.Id != 0
                && string.Equals(input, custom.ToString(), StringComparison.Ordinal);

            return validCustomEmoji ? custom : null;
        }

        if (StringInfo.ParseCombiningCharacters(input).Length != 1)
            return null;

        return Emoji.TryParse(input, out var unicode) ? unicode : null;
    }

    private static (ulong ChannelId, ulong MessageId)? ParseMessageLink(string text, ulong guildId)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
            return null;

        var discordHost = uri.Host is "discord.com" or "canary.discord.com" or "ptb.discord.com";

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !discordHost
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0)
        {
            return null;
        }

        var parts = uri.AbsolutePath.TrimEnd('/').Split('/');

        if (parts.Length != 5 || !string.Equals(parts[1], "channels", StringComparison.Ordinal))
            return null;

        if (!ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var linkedGuildId)
            || !ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var channelId)
            || !ulong.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var messageId))
        {
            return null;
        }

        if (linkedGuildId != guildId || channelId == 0 || messageId == 0)
            return null;

        return (channelId, messageId);
    }

    private static string? ValidateManageableRole(SocketGuild guild, SocketRole? role)
    {
        if (role is null || role.Id == guild.Id || role.IsManaged)
            return "The target must be an existing, ordinary role other than @everyone.";

        if (!guild.CurrentUser.GuildPermissions.ManageRoles || guild.CurrentUser.Hierarchy <= role.Position)
            return "The bot needs Manage Roles and a highest role above the target role.";

        return null;
    }

    private static string? ValidateRole(SocketGuild guild, SocketRole? role)
    {
        var manageableError = ValidateManageableRole(guild, role);

        if (manageableError is not null)
            return manageableError;

        if ((role!.Permissions.RawValue & (ulong)StaffPermissions) != 0)
            return "Roles with staff or moderation permissions cannot be assigned by reaction.";

        // Threads inherit their parent's overwrites; Discord.NET does not support reading their own.
        IEnumerable<OverwritePermissions?> overwrites = guild.Channels
           .Where(channel => channel is not SocketThreadChannel)
           .Select(channel => channel.GetPermissionOverwrite(role));

        var grantsStaffPermissions = overwrites
           .Any(overwrite => overwrite.HasValue && (overwrite.Value.AllowValue & (ulong)StaffPermissions) != 0);

        return grantsStaffPermissions
            ? "The role has staff or moderation permissions in a channel override and cannot be assigned by reaction."
            : null;
    }

    private async Task<IMessageChannel?> ReadChannelAsync(ulong guildId, ulong channelId, RequestOptions request)
    {
        IChannel? channel = client.GetChannel(channelId);
        channel ??= await client.Rest.GetChannelAsync(channelId, request);

        return channel is IGuildChannel guildChannel && guildChannel.GuildId == guildId
            ? channel as IMessageChannel
            : null;
    }

    private async Task UpdateRoleAsync(SocketGuild guild, IGuildUser member, RoleReaction mapping, bool added, RequestOptions request)
    {
        var role      = guild.GetRole(mapping.RoleId);
        var roleError = added ? ValidateRole(guild, role) : ValidateManageableRole(guild, role);

        if (roleError is not null)
        {
            logger.Warning(
                "Skipped role reaction in guild {GuildId} on message {MessageId} for member {UserId}, role {RoleId}: {Reason}",
                guild.Id,
                mapping.MessageId,
                member.Id,
                mapping.RoleId,
                roleError);

            return;
        }

        try
        {
            if (added)
                await member.AddRoleAsync(mapping.RoleId, request);
            else
                await member.RemoveRoleAsync(mapping.RoleId, request);
        }
        catch (HttpException exception)
        {
            logger.Warning(
                exception,
                "Could not update role {RoleId} for member {UserId} in guild {GuildId} from reaction on message {MessageId}",
                mapping.RoleId,
                member.Id,
                guild.Id,
                mapping.MessageId);
        }
    }

    private async Task<string> ConfigureReactionAsync(
        ulong guildId,
        IMessage message,
        ulong roleId,
        IEmote emote,
        RequestOptions request,
        CancellationToken ct)
    {
        var             emojiKey = GetEmojiKey(emote);
        await using var db       = await contextFactory.CreateDbContextAsync(ct);

        var mapping = await db.RoleReactions
           .SingleOrDefaultAsync(
                mapping => mapping.GuildId == guildId
                    && mapping.MessageId == message.Id
                    && mapping.EmojiKey == emojiKey,
                ct);

        if (mapping is not null && (mapping.RoleId != roleId || mapping.ChannelId != message.Channel.Id))
            return "That message and emoji already grant a different role. Remove the mapping first.";

        if (mapping is null)
        {
            mapping = new RoleReaction
            {
                GuildId   = guildId,
                ChannelId = message.Channel.Id,
                MessageId = message.Id,
                EmojiKey  = emojiKey,
                EmojiText = emote.ToString()!,
                RoleId    = roleId,
            };

            db.RoleReactions.Add(mapping);
            await db.SaveChangesAsync(ct);
        }

        try
        {
            await message.AddReactionAsync(emote, request);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning(exception, "Could not seed role reaction in guild {GuildId} on message {MessageId}", guildId, message.Id);

            return "The mapping is configured, but the bot could not add the reaction. Check emoji availability and bot permissions, then repeat this command.";
        }

        return $"Configured {emote} on the linked message to grant <@&{roleId}>. Removing an individual reaction removes the role.";
    }
}