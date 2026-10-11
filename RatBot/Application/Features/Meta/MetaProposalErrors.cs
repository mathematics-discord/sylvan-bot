namespace RatBot.Application.Features.Meta;

public static class MetaProposalErrors
{
    public static readonly Error PublicationFailed =
        Error.Conflict("MetaProposal.PublicationFailed", "Failed to publish the proposal.");

    public static readonly Error RetryCooldownActive = Error.Conflict(
        "MetaProposal.RetryCooldownActive",
        "That proposal publication retry is still on cooldown."
    );

    public static readonly Error SettingsNotConfigured = Error.NotFound(
        "MetaProposal.SettingsNotConfigured",
        "Meta proposal workflow settings are not fully configured."
    );

    public static readonly Error SuggestionNotTracked = Error.NotFound(
        "MetaProposal.SuggestionNotTracked",
        "This channel is not a tracked suggestion thread."
    );

    public static readonly Error ThreadNotTracked = Error.NotFound(
        "MetaProposal.ThreadNotTracked",
        "This thread is not tracked by the meta proposal workflow."
    );
}