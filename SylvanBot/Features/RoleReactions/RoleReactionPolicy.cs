namespace SylvanBot.Features.RoleReactions;

internal static class RoleReactionPolicy
{
    public static DotNext.Optional<string> ValidateManageableRole(SocketGuild guild, SocketRole role)
    {
        if (role.Id == guild.Id || role.IsManaged)
            return new DotNext.Optional<string>("The target must be an existing, ordinary role other than @everyone.");

        if (!guild.CurrentUser.GuildPermissions.ManageRoles || guild.CurrentUser.Hierarchy <= role.Position)
            return new DotNext.Optional<string>("The bot needs Manage Roles and a highest role above the target role.");

        return default;
    }

    public static DotNext.Optional<string> ValidateRole(SocketGuild guild, SocketRole role)
    {
        DotNext.Optional<string> manageableError = ValidateManageableRole(guild: guild, role: role);

        if (manageableError is { HasValue: true })
            return manageableError;

        if ((role.Permissions.RawValue & (ulong)SylvanBot.Features.RolePersistence.PersistentRolePolicy.StaffPermissions) != 0)
            return new DotNext.Optional<string>("Roles with staff or moderation permissions cannot be assigned by reaction.");

        // Threads inherit their parent's overwrites; Discord.NET does not support reading their own.
        IEnumerable<OverwritePermissions?> overwrites = guild
           .Channels
           .Where(channel => channel is not SocketThreadChannel)
           .Select(channel => channel.GetPermissionOverwrite(role));

        var grantsStaffPermissions = overwrites
           .Any(overwrite => overwrite.HasValue && (overwrite.Value.AllowValue & (ulong)SylvanBot.Features.RolePersistence.PersistentRolePolicy.StaffPermissions) != 0);

        return grantsStaffPermissions
            ? new DotNext.Optional<string>("The role has staff or moderation permissions in a channel override and cannot be assigned by reaction.")
            : default;
    }

    public static DotNext.Optional<string> ValidateChannelAccess(SocketGuild guild, SocketGuildUser moderator, IGuildChannel channel)
    {
        var moderatorPermissions = moderator.GetPermissions(channel);

        if (moderatorPermissions is not { ViewChannel: true, ReadMessageHistory: true })
            return new DotNext.Optional<string>("You must be able to view the linked channel and read its message history.");

        var botPermissions = guild.CurrentUser.GetPermissions(channel);

        return botPermissions is { ViewChannel: true, ReadMessageHistory: true, AddReactions: true }
            ? default
            : new DotNext.Optional<string>("The bot needs View Channel, Read Message History, and Add Reactions in the linked channel.");
    }
}