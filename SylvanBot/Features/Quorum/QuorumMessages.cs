namespace SylvanBot.Features.Quorum;

internal static class QuorumMessages
{
    internal const string ConfigurationIncomplete =
        "This channel has a quorum configuration, but no voter roles have been configured.";

    internal const string ConfigurationNotFound =
        "No quorum configuration exists for that channel.";

    internal const string MemberDataUnavailable =
        "Member data is currently unavailable, so quorum could not be calculated.";

    internal const string NoEligibleVoters =
        "No eligible voters were found.";
}