using Microsoft.EntityFrameworkCore;
using RatBot.Application.Features.Meta;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Features.Meta.Persistence;

namespace RatBot.Infrastructure.Features.Meta;

public sealed class MetaProposalService(BotDbContext db, ILogger logger)
{
    private static readonly TimeSpan PublicationRetryCooldown = TimeSpan.FromSeconds(10);
    private readonly ILogger _logger = logger.ForContext<MetaProposalService>();

    public async Task<ErrorOr<MetaProposalState>> ClearDeletedPollAsync(Guid stateId, CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.ClearDeletedPoll();

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state.Value;
    }

    public async Task<ErrorOr<MetaProposalState>> ClearDeletedPollByMessageAsync(ulong guildId, ulong pollMessageId,
        CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByPollMessageAsync(guildId, pollMessageId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.ClearDeletedPoll();

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state.Value;
    }

    public async Task<ErrorOr<MetaProposalState>> CloseSuggestionAsync(Guid stateId, CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.Close();

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state;
    }

    public async Task<ErrorOr<MetaProposalState>> CompletePollAsync(Guid stateId, bool submitWon,
        CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.CompletePoll(submitWon);

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state.Value;
    }

    public async Task<IReadOnlyList<MetaProposalState>> FindExpiredPollsAsync(DateTimeOffset nowUtc, int limit,
        CancellationToken ct = default)
    {
        return await db.MetaProposalStates.AsNoTracking().PollExpiringBeforeOrAt(nowUtc)
            .OrderBy(x => x.PollExpiresAtUtc).Take(limit).ToListAsync(ct);
    }

    public async Task ForgetDeletedUnsubmittedSuggestionAsync(ulong guildId, ulong suggestionThreadChannelId,
        CancellationToken ct = default)
    {
        MetaProposalState? state = await db.MetaProposalStates.ForSuggestionThread(guildId, suggestionThreadChannelId)
            .SingleOrDefaultAsync(ct);

        if (state is null)
            return;

        if (state.HasSubmittedProposal || state.IsTerminal)
            return;

        db.MetaProposalStates.Remove(state);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ErrorOr<Success>> ForgetUnsubmittedSuggestionAsync(Guid stateId, CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        MetaProposalState proposal = state.Value;

        if (proposal.HasSubmittedProposal || proposal.IsTerminal)
            return Error.Conflict("MetaProposal.CannotForgetSubmitted",
                "Only unsubmitted suggestion state can be forgotten.");

        db.MetaProposalStates.Remove(proposal);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<MetaProposalState>> GetByIdAsync(Guid stateId, CancellationToken ct = default)
    {
        return await FindByIdAsync(stateId, true, ct);
    }

    public async Task<ErrorOr<MetaProposalState>> GetByPollMessageAsync(ulong guildId, ulong pollMessageId,
        CancellationToken ct = default)
    {
        return await FindByPollMessageAsync(guildId, pollMessageId, true, ct);
    }

    public async Task<ErrorOr<MetaProposalState>> GetForAnyThreadAsync(ulong guildId, ulong threadChannelId,
        CancellationToken ct = default)
    {
        return await FindByAnyThreadAsync(guildId, threadChannelId, true, ct);
    }

    public async Task<ErrorOr<MetaProposalState>> GetForSuggestionThreadAsync(
        ulong guildId,
        ulong suggestionThreadChannelId,
        CancellationToken ct = default
    )
    {
        return await FindBySuggestionThreadAsync(guildId, suggestionThreadChannelId, true, ct);
    }

    public async Task<ErrorOr<MetaSuggestionSettings>> GetSettingsAsync(ulong guildId, CancellationToken ct = default)
    {
        MetaSuggestionSettings? settings =
            await db.MetaSuggestionSettings.AsNoTracking().ForGuild(guildId).SingleOrDefaultAsync(ct);

        if (settings is null)
            return MetaProposalErrors.SettingsNotConfigured;

        ErrorOr<Success> configured = settings.EnsureProposalWorkflowConfigured();

        return configured.IsError ? configured.Errors : settings;
    }

    public async Task<ErrorOr<MetaProposalState>> MarkPublicationRetryStartedAsync(
        Guid stateId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default
    )
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        if (state.Value.LastPublicationRetryAtUtc is { } lastRetry && nowUtc - lastRetry < PublicationRetryCooldown)
            return MetaProposalErrors.RetryCooldownActive;

        ErrorOr<Success> result = state.Value.MarkPublicationRetryStarted(nowUtc);

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state;
    }

    public async Task<ErrorOr<MetaProposalState>> MarkPublishedAsync(Guid stateId, ulong proposalThreadChannelId,
        CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.RecordPublished(proposalThreadChannelId);

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state;
    }

    public async Task RecordPollFinalizationRetryAsync(Guid stateId, CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return;

        ErrorOr<Success> result = state.Value.RecordPollFinalizationRetry();

        if (result.IsError)
            return;

        await db.SaveChangesAsync(ct);
    }

    public async Task<ErrorOr<MetaProposalState>> RecordPublicationFailureAsync(
        Guid stateId,
        ulong errorMessageId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default
    )
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.RecordPublicationFailure(errorMessageId, nowUtc);

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state;
    }

    public async Task<ErrorOr<MetaProposalState>> RecordPublishedAsync(Guid stateId, ulong proposalThreadChannelId,
        CancellationToken ct = default)
    {
        ErrorOr<MetaProposalState> state = await FindByIdAsync(stateId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.RecordPublished(proposalThreadChannelId);

        if (result.IsError)
            return MetaProposalErrors.PublicationFailed;

        await db.SaveChangesAsync(ct);
        return state;
    }

    public async Task<ErrorOr<MetaProposalState>> StartPollAsync(
        ulong guildId,
        ulong suggestionThreadChannelId,
        ulong proposalAuthorUserId,
        string title,
        string summary,
        string motivation,
        string specification,
        ulong pollMessageId,
        DateTimeOffset pollExpiresAtUtc,
        DateTimeOffset nowUtc,
        CancellationToken ct = default
    )
    {
        ErrorOr<MetaProposalState> state =
            await FindBySuggestionThreadAsync(guildId, suggestionThreadChannelId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.SuggestionNotTracked;

        ErrorOr<Success> result = state.Value.StartPoll(
            proposalAuthorUserId,
            title,
            summary,
            motivation,
            specification,
            pollMessageId,
            pollExpiresAtUtc,
            nowUtc
        );

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state.Value;
    }

    public async Task TrackSuggestionThreadAsync(
        ulong guildId,
        ulong suggestionThreadChannelId,
        ulong suggestionsForumChannelId,
        ulong originalThreadAuthorUserId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default
    )
    {
        MetaProposalState? existing = await db
            .MetaProposalStates.AsNoTracking()
            .ForSuggestionThread(guildId, suggestionThreadChannelId)
            .SingleOrDefaultAsync(ct);

        if (existing is not null)
            return;

        ErrorOr<MetaProposalState> create = MetaProposalState.TrackSuggestionThread(
            Guid.CreateVersion7(nowUtc),
            guildId,
            suggestionThreadChannelId,
            suggestionsForumChannelId,
            originalThreadAuthorUserId,
            nowUtc
        );

        if (create.IsError)
            return;

        db.MetaProposalStates.Add(create.Value);
        await db.SaveChangesAsync(ct);

        _logger.Information("Tracked meta suggestion thread {SuggestionThreadChannelId} in guild {GuildId}",
            suggestionThreadChannelId, guildId);
    }

    public async Task<ErrorOr<MetaProposalState>> VetoAsync(
        ulong guildId,
        ulong threadChannelId,
        ulong vetoedByUserId,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken ct = default
    )
    {
        ErrorOr<MetaProposalState> state = await FindByAnyThreadAsync(guildId, threadChannelId, false, ct);

        if (state.IsError)
            return MetaProposalErrors.ThreadNotTracked;

        ErrorOr<Success> result = state.Value.Veto(vetoedByUserId, reason, nowUtc);

        if (result.IsError)
            return result.Errors;

        await db.SaveChangesAsync(ct);
        return state;
    }

    private async Task<ErrorOr<MetaProposalState>> FindByAnyThreadAsync(ulong guildId, ulong threadChannelId,
        bool asNoTracking, CancellationToken ct)
    {
        IQueryable<MetaProposalState> query = db.MetaProposalStates.ForAnyThread(guildId, threadChannelId);
        MetaProposalState? state = await (asNoTracking ? query.AsNoTracking() : query).SingleOrDefaultAsync(ct);
        return state is null ? MetaProposalErrors.ThreadNotTracked : state;
    }

    private async Task<ErrorOr<MetaProposalState>> FindByIdAsync(Guid stateId, bool asNoTracking, CancellationToken ct)
    {
        IQueryable<MetaProposalState> query = db.MetaProposalStates.ForId(stateId);
        MetaProposalState? state = await (asNoTracking ? query.AsNoTracking() : query).SingleOrDefaultAsync(ct);
        return state is null ? MetaProposalErrors.ThreadNotTracked : state;
    }

    private async Task<ErrorOr<MetaProposalState>> FindByPollMessageAsync(ulong guildId, ulong pollMessageId,
        bool asNoTracking, CancellationToken ct)
    {
        IQueryable<MetaProposalState> query = db.MetaProposalStates.ForPollMessage(guildId, pollMessageId);
        MetaProposalState? state = await (asNoTracking ? query.AsNoTracking() : query).SingleOrDefaultAsync(ct);
        return state is null ? MetaProposalErrors.ThreadNotTracked : state;
    }

    private async Task<ErrorOr<MetaProposalState>> FindBySuggestionThreadAsync(
        ulong guildId,
        ulong suggestionThreadChannelId,
        bool asNoTracking,
        CancellationToken ct
    )
    {
        IQueryable<MetaProposalState> query =
            db.MetaProposalStates.ForSuggestionThread(guildId, suggestionThreadChannelId);
        MetaProposalState? state = await (asNoTracking ? query.AsNoTracking() : query).SingleOrDefaultAsync(ct);
        return state is null ? MetaProposalErrors.SuggestionNotTracked : state;
    }
}