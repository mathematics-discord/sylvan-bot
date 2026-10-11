#pragma warning disable MA0048
namespace RatBot.Domain.Features.Quorum;

public sealed record QuorumConfiguration
{
    private QuorumConfiguration(QuorumConfigurationId id, QuorumScope scope, QuorumProportion proportion,
        QuorumVoterRoleSet voterRoles)
    {
        Id = id;
        Scope = scope;
        Proportion = proportion;
        VoterRoles = voterRoles;
    }

    public QuorumConfigurationId Id { get; }

    public bool IsComplete => !VoterRoles.IsEmpty;

    public QuorumProportion Proportion { get; private init; }

    public QuorumScope Scope { get; }

    public QuorumVoterRoleSet VoterRoles { get; private init; }

    public static QuorumConfiguration Create(QuorumScope scope, QuorumProportion proportion)
    {
        return new QuorumConfiguration(QuorumConfigurationId.New(), scope, proportion, new QuorumVoterRoleSet([]));
    }

    public static QuorumConfiguration Rehydrate(
        QuorumConfigurationId id,
        QuorumScope scope,
        QuorumProportion proportion,
        QuorumVoterRoleSet voterRoles
    )
    {
        return new QuorumConfiguration(id, scope, proportion, voterRoles);
    }

    public QuorumConfiguration AddRole(ulong roleId)
    {
        return this with { VoterRoles = VoterRoles.Add(roleId) };
    }

    public QuorumConfiguration RemoveRole(ulong roleId)
    {
        return this with { VoterRoles = VoterRoles.Remove(roleId) };
    }

    public QuorumConfiguration WithProportion(QuorumProportion proportion)
    {
        return this with { Proportion = proportion };
    }
}

public readonly record struct QuorumConfigurationId(Guid Value)
{
    public static QuorumConfigurationId New()
    {
        return new QuorumConfigurationId(Guid.CreateVersion7());
    }
}

public readonly record struct QuorumProportion
{
    private QuorumProportion(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static ErrorOr<QuorumProportion> Create(decimal value)
    {
        return value is > 0 and <= 1
            ? new QuorumProportion(value)
            : Error.Validation(description: "Quorum proportion must be greater than 0 and at most 1.");
    }
}