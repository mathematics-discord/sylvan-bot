namespace RatBot.Features.Meta.Commands;

internal static class MetaCommandPermissions
{
    public static bool IsAuthorOrAdmin(MetaProposalState state, IGuildUser user)
    {
        return IsAdmin(user) || user.Id == state.OriginalThreadAuthorUserId;
    }

    public static bool IsCabinet(MetaSuggestionSettings settings, IGuildUser user)
    {
        return IsAdmin(user) || user.RoleIds.Contains(settings.CabinetRoleId) ||
               user.RoleIds.Contains(settings.CabinetChairRoleId);
    }

    public static bool IsChairOrAdmin(MetaSuggestionSettings settings, IGuildUser user)
    {
        return IsAdmin(user) || user.RoleIds.Contains(settings.CabinetChairRoleId);
    }

    public static bool IsOwnerOrChair(MetaSuggestionSettings settings, IGuildUser user)
    {
        return IsAdmin(user) || user.Guild.OwnerId == user.Id || user.RoleIds.Contains(settings.CabinetChairRoleId);
    }

    private static bool IsAdmin(IGuildUser user)
    {
        return user.GuildPermissions.Administrator;
    }
}