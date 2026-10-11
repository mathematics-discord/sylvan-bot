namespace RatBot.Domain.Features.Quorum;

public sealed record QuorumVoterRoleSet
{
    public QuorumVoterRoleSet(IEnumerable<ulong> roleIds)
    {
        RoleIds = roleIds.ToImmutableHashSet();
    }

    public bool IsEmpty => RoleIds.IsEmpty;

    public ImmutableHashSet<ulong> RoleIds { get; }

    public QuorumVoterRoleSet Add(ulong roleId)
    {
        return new QuorumVoterRoleSet(RoleIds.Add(roleId));
    }

    public QuorumVoterRoleSet Remove(ulong roleId)
    {
        return new QuorumVoterRoleSet(RoleIds.Remove(roleId));
    }
}