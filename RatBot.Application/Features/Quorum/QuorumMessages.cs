namespace RatBot.Application.Features.Quorum;

public static class QuorumMessages
{
    public const string ConfigurationIncomplete = "This channel has a quorum configuration, but no voter roles have been configured.";
    public const string ConfigurationNotFound = "No quorum configuration exists for that channel.";
    public const string MemberDataUnavailable = "Member data is currently unavailable, so quorum could not be calculated.";
    public const string NoEligibleVoters = "No eligible voters were found.";
    public const string InvalidProportion = "Quorum proportion must be greater than 0 and at most 1.";
}