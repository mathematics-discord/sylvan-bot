namespace SylvanBot.Application.Features.RoleColours;

/// <summary>Preserves a valid sole selection, removing only colours owned by current or former grants.</summary>
public static class RoleColourSelection
{
    public static IReadOnlySet<ulong> Removals(
        IReadOnlySet<ulong> worn,
        bool holdsGrant,
        IReadOnlySet<ulong> eligible,
        IReadOnlySet<ulong> previouslyGranted)
    {
        if (!holdsGrant)
            return worn.Where(previouslyGranted.Contains).ToHashSet();

        var validSelection = worn.Count == 1 && worn.All(eligible.Contains);

        return validSelection
            ? new HashSet<ulong>()
            : new HashSet<ulong>(worn);
    }
}