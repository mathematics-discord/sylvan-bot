using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.Quorum;

public static class QuorumCalculator
{
    public static QuorumCalculation Calculate(
        IReadOnlyCollection<QuorumMember> members,
        IReadOnlySet<ulong> voterRoleIds,
        decimal proportion)
    {
        if (voterRoleIds.Count == 0)
            throw new BotCommandException(QuorumMessages.ConfigurationIncomplete);

        HashSet<ulong> eligibleUserIds = [];

        foreach (var member in members)
        {
            if (member.IsBot)
                continue;

            var hasVoterRole = member.RoleIds.Any(voterRoleIds.Contains);

            if (hasVoterRole)
                eligibleUserIds.Add(member.UserId);
        }

        var eligibleVoterCount = eligibleUserIds.Count;

        if (eligibleVoterCount == 0)
            throw new BotCommandException(QuorumMessages.NoEligibleVoters);

        var requiredVotes = (int)Math.Ceiling(eligibleVoterCount * proportion);

        return new QuorumCalculation(EligibleVoterCount: eligibleVoterCount, Proportion: proportion, RequiredVotes: requiredVotes);
    }
}

public readonly record struct QuorumCalculation(int EligibleVoterCount, decimal Proportion, int RequiredVotes);