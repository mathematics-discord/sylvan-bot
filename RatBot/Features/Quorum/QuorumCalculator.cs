namespace RatBot.Features.Quorum;

public static class QuorumCalculator
{
    public static async Task<ErrorOr<(int EligibleVoterCount, int RequiredVotes)>> CalculateAsync(
        IGuild gld,
        QuorumConfiguration configuration,
        ILogger logger,
        CancellationToken ct)
    {
        if (configuration.RoleIds.IsEmpty)
            return QuorumErrors.ConfigurationIncomplete;

        IReadOnlyCollection<IGuildUser> members;

        try
        {
            // In Discord.NET 3.20.1, AllowDownload uses the complete socket cache when available;
            // otherwise it awaits every page of GetUsersAsync. CacheOnly can return a partial electorate.
            members = await gld.GetUsersAsync(CacheMode.AllowDownload, new RequestOptions { CancelToken = ct });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger
                .ForContext("GuildId", gld.Id)
                .ForContext("ChannelId", configuration.ChannelId)
                .Warning(ex, "Failed to retrieve quorum member data");

            return QuorumErrors.MemberDataUnavailable;
        }

        int eligibleVoterCount = members
            .Where(member => !member.IsBot && member.RoleIds.Any(configuration.RoleIds.Contains))
            .Select(member => member.Id)
            .Distinct()
            .Count();

        if (eligibleVoterCount == 0)
            return QuorumErrors.NoEligibleVoters;

        int requiredVotes = (int)Math.Ceiling(eligibleVoterCount * configuration.Proportion);

        return (eligibleVoterCount, requiredVotes);
    }
}