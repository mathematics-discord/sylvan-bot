namespace RatBot.Features.Quorum;

public abstract record QuorumRoleUpdate(QuorumScope Scope, ulong RoleId)
{
    public static QuorumRoleUpdate FromOption(QuorumScope scope, ulong roleId, bool shouldAdd)
    {
        return shouldAdd ? new Add(scope, roleId) : new Remove(scope, roleId);
    }

    public sealed record Add(QuorumScope Scope, ulong RoleId) : QuorumRoleUpdate(Scope, RoleId);

    public sealed record Remove(QuorumScope Scope, ulong RoleId) : QuorumRoleUpdate(Scope, RoleId);
}