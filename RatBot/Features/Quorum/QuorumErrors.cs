namespace RatBot.Features.Quorum;

public static class QuorumErrors
{
    public static Error ConfigurationIncomplete =>
        Error.Validation(
            description: "This channel has a quorum configuration, but no voter roles have been configured.");

    public static Error ConfigurationNotFound =>
        Error.NotFound(description: "No quorum configuration exists for that channel.");

    public static Error MemberDataUnavailable =>
        Error.Failure(description: "Member data is currently unavailable, so quorum could not be calculated.");

    public static Error NoEligibleVoters =>
        Error.Validation(description: "No eligible voters were found.");
}