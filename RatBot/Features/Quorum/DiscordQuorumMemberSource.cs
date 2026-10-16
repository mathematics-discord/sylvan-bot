namespace RatBot.Features.Quorum;

public sealed class DiscordQuorumMemberSource(
    DiscordSocketClient client,
    DiscordQuorumMemberIndex index,
    ILogger logger) : IQuorumMemberSource
{
    private readonly ILogger _logger = logger.ForContext<DiscordQuorumMemberSource>();

    public async Task<ErrorOr<int>> CountEligibleVotersAsync(QuorumScope scope, ImmutableHashSet<ulong> roleIds,
        CancellationToken ct)
    {
        if (roleIds.IsEmpty)
            return 0;

        SocketGuild? guild = client.GetGuild(scope.GuildId);

        if (guild is null)
            return QuorumErrors.MemberDataUnavailable;

        try
        {
            await index.EnsureTrackingAsync(guild, roleIds, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger
                .ForContext("GuildId", scope.GuildId)
                .ForContext("ChannelId", scope.ChannelId)
                .ForContext("ConfiguredRoleCount", roleIds.Count)
                .Warning(ex, "Failed to ensure quorum member index is ready");

            return QuorumErrors.MemberDataUnavailable;
        }

        return index.TryCountEligibleVoters(scope.GuildId, roleIds, out int eligibleVoterCount)
            ? eligibleVoterCount
            : QuorumErrors.MemberDataUnavailable;
    }
}