#pragma warning disable MA0048
using System.Diagnostics;

namespace RatBot.Features.Quorum;

public readonly record struct QuorumCalculation(int EligibleVoterCount, QuorumProportion Proportion, int RequiredVotes);

public sealed class QuorumOperations(IQuorumConfigurationStore configurations, IQuorumMemberSource members)
{
    private static ErrorOr<QuorumCalculation> Calculate(int eligibleVoterCount, QuorumProportion proportion)
    {
        if (eligibleVoterCount <= 0)
            return Error.Validation(description: "No eligible voters were found.");

        int requiredVotes = (int)Math.Ceiling(eligibleVoterCount * proportion.Value);

        return new QuorumCalculation(eligibleVoterCount, proportion, requiredVotes);
    }

    public async Task<ErrorOr<QuorumCalculation>> CalculateAsync(QuorumScope scope, CancellationToken ct)
    {
        ErrorOr<QuorumConfiguration> configurationResult = await configurations.GetAsync(scope, ct);

        if (configurationResult.IsError)
            return configurationResult.Errors;

        QuorumConfiguration configuration = configurationResult.Value;

        if (!configuration.IsComplete)
            return QuorumErrors.ConfigurationIncomplete;

        ErrorOr<int> eligibleVoterCount =
            await members.CountEligibleVotersAsync(configuration.Scope, configuration.VoterRoles.RoleIds, ct);

        if (eligibleVoterCount.IsError)
            return eligibleVoterCount.Errors;

        return Calculate(eligibleVoterCount.Value, configuration.Proportion);
    }

    public Task<ErrorOr<QuorumConfiguration>> InspectAsync(QuorumScope scope, CancellationToken ct)
    {
        return configurations.GetAsync(scope, ct);
    }

    public async Task<ErrorOr<QuorumRegistration>> RegisterAsync(QuorumScope scope, decimal proportionValue,
        CancellationToken ct)
    {
        ErrorOr<QuorumProportion> proportion = QuorumProportion.Create(proportionValue);

        return proportion.IsError ? proportion.Errors : await configurations.RegisterAsync(scope, proportion.Value, ct);
    }

    public Task<ErrorOr<Deleted>> RemoveAsync(QuorumScope scope, CancellationToken ct)
    {
        return configurations.DeleteAsync(scope, ct);
    }

    public async Task<ErrorOr<QuorumConfiguration>> UpdateRoleAsync(QuorumRoleUpdate update, CancellationToken ct)
    {
        ErrorOr<QuorumConfiguration> configurationResult = await configurations.GetAsync(update.Scope, ct);

        if (configurationResult.IsError)
            return configurationResult.Errors;

        QuorumConfiguration updated = update switch
        {
            QuorumRoleUpdate.Add add => configurationResult.Value.AddRole(add.RoleId),
            QuorumRoleUpdate.Remove remove => configurationResult.Value.RemoveRole(remove.RoleId),
            _ => throw new UnreachableException()
        };

        return await configurations.SaveAsync(updated, ct);
    }
}