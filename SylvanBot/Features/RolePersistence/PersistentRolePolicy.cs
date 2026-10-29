namespace SylvanBot.Features.RolePersistence;

internal static class PersistentRolePolicy
{
    internal const GuildPermission StaffPermissions =
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

    internal static bool IsSafe(ulong permissions, IEnumerable<ulong> overrides) =>
        (permissions & (ulong)StaffPermissions) == 0 && overrides.All(value => (value & (ulong)StaffPermissions) == 0);

    internal static bool IsSafe(SocketGuild guild, SocketRole role) =>
        role.Id != guild.Id
        && !role.IsManaged
        && IsSafe(
            role.Permissions.RawValue,
            guild
                .Channels.Where(channel => channel is not SocketThreadChannel)
                .Select(channel => channel.GetPermissionOverwrite(role)?.AllowValue ?? 0)
        );
}
