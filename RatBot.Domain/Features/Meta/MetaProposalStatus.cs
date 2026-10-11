namespace RatBot.Domain.Features.Meta;

public enum MetaProposalStatus
{
    SuggestionOpen = 0,
    PollActive = 1,
    PublicationPending = 2,
    PublicationRetry = 3,
    Published = 4,
    Vetoed = 5,
    Closed = 6
}