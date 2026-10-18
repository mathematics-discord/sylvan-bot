namespace RatBot.Features.Quorum;

public static class QuorumCalculator
{
    public static async Task<(int EligibleVoterCount, int RequiredVotes)> CalculateAsync(
        IGuild gld,
        QuorumConfiguration configuration,
        CancellationToken ct)
    {
        if (configuration.RoleIds.IsEmpty)
            throw new ArgumentException(QuorumMessages.ConfigurationIncomplete, nameof(configuration));

        // AllowDownload retrieves the complete electorate, including every REST page when needed.
        IReadOnlyCollection<IGuildUser> members = await gld.GetUsersAsync(
            CacheMode.AllowDownload, new RequestOptions { CancelToken = ct });

        var eligibleVoterCount = members
            .Where(member => !member.IsBot && member.RoleIds.Any(configuration.RoleIds.Contains))
            .Select(member => member.Id)
            .Distinct()
            .Count();

        var requiredVotes = (int)Math.Ceiling(eligibleVoterCount * configuration.Proportion);

        return (eligibleVoterCount, requiredVotes);
    }
}