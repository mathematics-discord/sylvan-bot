using RatBot.Domain.Features.Quorum;

namespace RatBot.Application.Features.Quorum;

/// <summary>
/// Provides guild member data required to calculate quorum.
/// </summary>
public interface IQuorumMemberSource
{
    /// <summary>Counts the distinct non-bot guild members who have at least one of the supplied voter roles.</summary>
    /// <param name="scope">The quorum scope identifying the guild channel whose quorum is being calculated. </param>
    /// <param name="roleIds">The configured voter role IDs for the quorum scope. The set should already be deduplicated.</param>
    /// <param name="ct">A cancellation token for the Discord member fetch.</param>
    /// <returns>The number of distinct eligible voters, or an expected failure if member data cannot be fetched.</returns>
    Task<ErrorOr<int>> CountEligibleVotersAsync(QuorumScope scope, ImmutableHashSet<ulong> roleIds,
        CancellationToken ct);
}