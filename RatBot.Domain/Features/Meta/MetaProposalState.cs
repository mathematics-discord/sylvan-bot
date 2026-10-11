namespace RatBot.Domain.Features.Meta;

public sealed class MetaProposalState
{
    public const int MaxFailedPollAttempts = 3;
    public const int MaxPublicationRetryFailuresBeforePing = 10;
    public const int MaxTitleLength = 100;

    private MetaProposalState()
    {
    }

    public int FailedPollAttempts { get; private set; }
    public ulong GuildId { get; private set; }

    public bool HasProposalText =>
        ProposalAuthorUserId is not null
        && !string.IsNullOrWhiteSpace(ProposalTitle)
        && !string.IsNullOrWhiteSpace(Summary)
        && !string.IsNullOrWhiteSpace(Motivation)
        && !string.IsNullOrWhiteSpace(Specification)
        && ProposedAtUtc is not null;

    public bool HasSubmittedProposal =>
        Status is not MetaProposalStatus.SuggestionOpen and not MetaProposalStatus.PollActive;

    public Guid Id { get; private set; }

    public bool IsTerminal => Status is MetaProposalStatus.Vetoed or MetaProposalStatus.Closed;
    public DateTimeOffset? LastPublicationRetryAtUtc { get; private set; }
    public string? Motivation { get; private set; }
    public ulong OriginalThreadAuthorUserId { get; private set; }
    public DateTimeOffset? PollExpiresAtUtc { get; private set; }
    public int PollFinalizationRetries { get; private set; }
    public ulong? PollMessageId { get; private set; }
    public ulong? ProposalAuthorUserId { get; private set; }
    public ulong? ProposalThreadChannelId { get; private set; }
    public string? ProposalTitle { get; private set; }
    public DateTimeOffset? ProposedAtUtc { get; private set; }
    public ulong? PublicationErrorMessageId { get; private set; }
    public int PublicationRetryFailures { get; private set; }
    public string? Specification { get; private set; }
    public MetaProposalStatus Status { get; private set; }
    public ulong SuggestionsForumChannelId { get; private set; }
    public ulong SuggestionThreadChannelId { get; private set; }
    public string? Summary { get; private set; }
    public DateTimeOffset TrackedAtUtc { get; private set; }
    public DateTimeOffset? VetoedAtUtc { get; private set; }
    public ulong? VetoedByUserId { get; private set; }
    public string? VetoReason { get; private set; }

    public static ErrorOr<MetaProposalState> TrackSuggestionThread(
        Guid id,
        ulong guildId,
        ulong suggestionThreadChannelId,
        ulong suggestionsForumChannelId,
        ulong originalThreadAuthorUserId,
        DateTimeOffset trackedAtUtc
    )
    {
        if (id == Guid.Empty)
            return Error.Validation("MetaProposal.IdRequired", "A proposal state id is required.");

        if (guildId == 0)
            return RequiredId(nameof(GuildId));

        if (suggestionThreadChannelId == 0)
            return RequiredId(nameof(SuggestionThreadChannelId));

        if (suggestionsForumChannelId == 0)
            return RequiredId(nameof(SuggestionsForumChannelId));

        if (originalThreadAuthorUserId == 0)
            return RequiredId(nameof(OriginalThreadAuthorUserId));

        return new MetaProposalState
        {
            Id = id,
            GuildId = guildId,
            SuggestionThreadChannelId = suggestionThreadChannelId,
            SuggestionsForumChannelId = suggestionsForumChannelId,
            OriginalThreadAuthorUserId = originalThreadAuthorUserId,
            TrackedAtUtc = trackedAtUtc,
            Status = MetaProposalStatus.SuggestionOpen
        };
    }

    private static Error RequiredFieldMissing(string fieldName)
    {
        return Error.Validation($"MetaProposal.{fieldName}Required",
            $"Meta proposal {fieldName.ToLowerInvariant()} is required.");
    }

    private static Error RequiredId(string fieldName)
    {
        return Error.Validation($"MetaProposal.{fieldName}Required",
            $"A valid {fieldName.ToLowerInvariant()} is required.");
    }

    public ErrorOr<Success> ClearDeletedPoll()
    {
        if (Status is not MetaProposalStatus.PollActive)
            return Result.Success;

        PollMessageId = null;
        PollExpiresAtUtc = null;
        PollFinalizationRetries = 0;
        Status = MetaProposalStatus.SuggestionOpen;
        return Result.Success;
    }

    public ErrorOr<Success> Close()
    {
        if (IsTerminal)
            return Result.Success;

        Status = MetaProposalStatus.Closed;
        PollMessageId = null;
        PollExpiresAtUtc = null;
        PollFinalizationRetries = 0;
        return Result.Success;
    }

    public ErrorOr<Success> CompletePoll(bool submitWon)
    {
        if (Status is not MetaProposalStatus.PollActive)
            return Error.Conflict("MetaProposal.NoActivePoll", "There is no active proposal poll.");

        PollMessageId = null;
        PollExpiresAtUtc = null;
        PollFinalizationRetries = 0;

        if (submitWon)
        {
            Status = MetaProposalStatus.PublicationPending;
            return Result.Success;
        }

        FailedPollAttempts++;

        Status = FailedPollAttempts >= MaxFailedPollAttempts
            ? MetaProposalStatus.Closed
            : MetaProposalStatus.SuggestionOpen;

        return Result.Success;
    }

    public ErrorOr<Success> MarkPublicationRetryStarted(DateTimeOffset attemptedAtUtc)
    {
        if (Status is not MetaProposalStatus.PublicationRetry and not MetaProposalStatus.PublicationPending)
            return Error.Conflict("MetaProposal.NotAwaitingPublication", "This proposal is not awaiting publication.");

        LastPublicationRetryAtUtc = attemptedAtUtc;
        return Result.Success;
    }

    public ErrorOr<Success> RecordPollFinalizationRetry()
    {
        if (Status is not MetaProposalStatus.PollActive)
            return Error.Conflict("MetaProposal.NoActivePoll", "There is no active proposal poll.");

        PollFinalizationRetries++;
        return Result.Success;
    }

    public ErrorOr<Success> RecordPublicationFailure(ulong errorMessageId, DateTimeOffset attemptedAtUtc)
    {
        if (Status is not MetaProposalStatus.PublicationPending and not MetaProposalStatus.PublicationRetry)
            return Error.Conflict("MetaProposal.NotAwaitingPublication", "This proposal is not awaiting publication.");

        if (errorMessageId == 0)
            return RequiredId(nameof(PublicationErrorMessageId));

        PublicationRetryFailures++;
        PublicationErrorMessageId = errorMessageId;
        LastPublicationRetryAtUtc = attemptedAtUtc;
        Status = MetaProposalStatus.PublicationRetry;
        return Result.Success;
    }

    public ErrorOr<Success> RecordPublished(ulong proposalThreadChannelId)
    {
        if (proposalThreadChannelId == 0)
            return RequiredId(nameof(ProposalThreadChannelId));

        ProposalThreadChannelId = proposalThreadChannelId;
        PublicationErrorMessageId = null;
        PublicationRetryFailures = 0;
        Status = MetaProposalStatus.Published;
        return Result.Success;
    }

    public ErrorOr<Success> StartPoll(
        ulong proposalAuthorUserId,
        string title,
        string summary,
        string motivation,
        string specification,
        ulong pollMessageId,
        DateTimeOffset pollExpiresAtUtc,
        DateTimeOffset proposedAtUtc
    )
    {
        if (Status is MetaProposalStatus.PollActive)
            return Error.Conflict("MetaProposal.PollAlreadyActive", "A proposal poll is already active.");

        if (IsTerminal || HasSubmittedProposal)
            return Error.Conflict("MetaProposal.NotOpen", "This suggestion thread is no longer open for proposals.");

        if (FailedPollAttempts >= MaxFailedPollAttempts)
            return Error.Conflict("MetaProposal.AttemptsExhausted",
                "This suggestion thread has no proposal attempts left.");

        title = title.Trim();
        summary = summary.Trim();
        motivation = motivation.Trim();
        specification = specification.Trim();

        if (proposalAuthorUserId == 0)
            return RequiredId(nameof(ProposalAuthorUserId));

        if (string.IsNullOrWhiteSpace(title))
            return RequiredFieldMissing(nameof(ProposalTitle));

        if (title.Length > MaxTitleLength)
            return Error.Validation("MetaProposal.TitleTooLong",
                $"Proposal title must be at most {MaxTitleLength} characters.");

        if (string.IsNullOrWhiteSpace(summary))
            return RequiredFieldMissing(nameof(Summary));

        if (string.IsNullOrWhiteSpace(motivation))
            return RequiredFieldMissing(nameof(Motivation));

        if (string.IsNullOrWhiteSpace(specification))
            return RequiredFieldMissing(nameof(Specification));

        if (pollMessageId == 0)
            return RequiredId(nameof(PollMessageId));

        ProposalAuthorUserId = proposalAuthorUserId;
        ProposalTitle = title;
        Summary = summary;
        Motivation = motivation;
        Specification = specification;
        ProposedAtUtc = proposedAtUtc;
        PollMessageId = pollMessageId;
        PollExpiresAtUtc = pollExpiresAtUtc;
        PollFinalizationRetries = 0;
        Status = MetaProposalStatus.PollActive;

        return Result.Success;
    }

    public ErrorOr<Success> Veto(ulong vetoedByUserId, string reason, DateTimeOffset vetoedAtUtc)
    {
        if (IsTerminal)
            return Error.Conflict("MetaProposal.Terminal", "This proposal is already terminal.");

        reason = reason.Trim();

        if (vetoedByUserId == 0)
            return RequiredId(nameof(VetoedByUserId));

        if (string.IsNullOrWhiteSpace(reason))
            return RequiredFieldMissing(nameof(VetoReason));

        VetoedByUserId = vetoedByUserId;
        VetoedAtUtc = vetoedAtUtc;
        VetoReason = reason;
        Status = MetaProposalStatus.Vetoed;
        PollMessageId = null;
        PollExpiresAtUtc = null;
        PollFinalizationRetries = 0;
        return Result.Success;
    }
}