namespace SylvanBot.Features.RoleReactions;

using DotNext;

internal static class RoleReactionPolicy
{
    private const GuildPermission StaffPermissions =
        GuildPermission.Administrator
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

    public static Optional<string> ValidateManageableRole(SocketGuild guild, SocketRole role)
    {
        if (role.Id == guild.Id || role.IsManaged)
            return new Optional<string>("The target must be an existing, ordinary role other than @everyone.");

        if (!guild.CurrentUser.GuildPermissions.ManageRoles || guild.CurrentUser.Hierarchy <= role.Position)
            return new Optional<string>("The bot needs Manage Roles and a highest role above the target role.");

        return default;
    }

    public static Optional<string> ValidateRole(SocketGuild guild, SocketRole role)
    {
        Optional<string> manageableError = ValidateManageableRole(guild, role);

        if (manageableError is { HasValue: true })
            return manageableError;

        if ((role.Permissions.RawValue & (ulong)StaffPermissions) != 0)
            return new Optional<string>("Roles with staff or moderation permissions cannot be assigned by reaction.");

        // Threads inherit their parent's overwrites; Discord.NET does not support reading their own.
        IEnumerable<OverwritePermissions?> overwrites = guild
            .Channels
            .Where(channel => channel is not SocketThreadChannel)
            .Select(channel => channel.GetPermissionOverwrite(role));

        var grantsStaffPermissions = overwrites
            .Any(overwrite => overwrite.HasValue && (overwrite.Value.AllowValue & (ulong)StaffPermissions) != 0);

        return grantsStaffPermissions
            ? new Optional<string>("The role has staff or moderation permissions in a channel override and cannot be assigned by reaction.")
            : default;
    }

    public static Optional<string> ValidateChannelAccess(SocketGuild guild, SocketGuildUser moderator, IGuildChannel channel)
    {
        var moderatorPermissions = moderator.GetPermissions(channel);

        if (moderatorPermissions is not { ViewChannel: true, ReadMessageHistory: true })
            return new Optional<string>("You must be able to view the linked channel and read its message history.");

        var botPermissions = guild.CurrentUser.GetPermissions(channel);

        return botPermissions is { ViewChannel: true, ReadMessageHistory: true, AddReactions: true }
            ? default
            : new Optional<string>("The bot needs View Channel, Read Message History, and Add Reactions in the linked channel.");
    }
}