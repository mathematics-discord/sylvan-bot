using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.Tickets;

public sealed partial class TicketCoordinator
{
    private async Task RecordAuditAsync(BotDbContext db, TicketObservation observation, IReadOnlyDictionary<Guid, ulong> operationAudits, bool automodEnabled, Func<ulong, Task<bool>> resolveReviewPermissionAsync, CancellationToken ct)
    {
        var alreadyRecorded = await db.Tickets
            .AnyAsync(
                ticket => ticket.GuildId == observation.GuildId
                    && ticket.SourceAuditId == observation.AuditId
                    && ticket.Action == observation.Action
                    && ticket.RoleId == observation.RoleId,
                ct);

        if (alreadyRecorded)
            return;

        IQueryable<Ticket> matching = db.Tickets
            .Where(ticket => ticket.GuildId == observation.GuildId
                       && ticket.SubjectUserId == observation.SubjectUserId
                       && ticket.Action == observation.Action
                       && ticket.RoleId == observation.RoleId);

        var latest = await matching
            .Where(ticket => ticket.SourceAuditId != null)
            .OrderByDescending(ticket => ticket.SourceAuditId)
            .FirstOrDefaultAsync(ct);

        var operation = observation.OperationId.HasValue
            ? await matching.SingleOrDefaultAsync(ticket => ticket.Pending != null && ticket.Pending.Id == observation.OperationId.Value, ct)
            : null;

        var older = observation.Action != TicketAction.Kick && latest is not null && latest.LastAuditId >= observation.AuditId;
        var replayedTimeout = observation.Action == TicketAction.Timeout && older && operation is null;

        // Continuations have no source row. Replays must not create historical tickets.
        if (replayedTimeout)
            return;

        var completedTimeoutOperation = observation is { Action: TicketAction.Timeout, OperationId.HasValue: true }
            && operation is null;

        if (completedTimeoutOperation)
        {
            if (latest is null || older)
                return;

            latest.LastAuditId = observation.AuditId;
            await db.SaveChangesAsync(ct);
            return;
        }

        var confirmsOperation = operation is not null && operation.LastAuditId < observation.AuditId;
        var rejectedConfirmation = confirmsOperation && !await ConfirmAuditOperationAsync(operation!, observation, resolveReviewPermissionAsync);

        if (rejectedConfirmation)
            return;

        await ApplyAuditAsync(db, observation, operationAudits, automodEnabled, latest, operation, older, resolveReviewPermissionAsync, ct);
    }

    private static async Task<bool> ConfirmAuditOperationAsync(Ticket ticket, TicketObservation observation, Func<ulong, Task<bool>> resolveReviewPermissionAsync)
    {
        var pending = ticket.Pending!;
        var changesTimeout = pending is { Kind: TicketOperationKind.SetTimeoutExpiry, ExpiryUtc: not null };

        if (changesTimeout)
        {
            var timeoutExpiry = pending.ExpiryUtc!.Value;
            var nativeExpiry = pending.NativeExpiryUtc ?? timeoutExpiry;

            var matchesRequestedExpiry = timeoutExpiry <= observation.CreatedAtUtc
                ? observation.Removed
                : observation.Timeout.TryGet(out var applied) && applied == nativeExpiry;

            var suspendedRenewal = ticket.ExpirySuspended && pending.RequestedBy is null;
            var canConfirm = matchesRequestedExpiry && ticket.Status == TicketStatus.Active && !suspendedRenewal;

            if (!canConfirm)
                return false;

            var requesterCanReview = pending.RequestedBy is { } requester
                && await resolveReviewPermissionAsync(requester);

            ticket.ConfirmTimeoutChange(observation.CreatedAtUtc, requesterCanReview);
        }

        var endsRestriction = observation.Removed && ticket.Status != TicketStatus.Superseded;

        if (endsRestriction)
            ticket.End(ticket.ExpiresAtUtc <= observation.CreatedAtUtc ? TicketStatus.Expired : TicketStatus.Reverted);

        ticket.Pending = null;
        ticket.LastAuditId = observation.AuditId;
        ticket.Record(TicketHistoryAction.AuditObserved, observation.ModeratorUserId, observation.CreatedAtUtc);
        return true;
    }

    private async Task ApplyAuditAsync(
        BotDbContext db,
        TicketObservation observation,
        IReadOnlyDictionary<Guid, ulong> operationAudits,
        bool automodEnabled,
        Ticket? latest,
        Ticket? operation,
        bool older,
        Func<ulong, Task<bool>> resolveReviewPermissionAsync,
        CancellationToken ct)
    {
        var config = await db.TicketConfigurations.SingleAsync(value => value.GuildId == observation.GuildId, ct);

        if (observation.RestoredTicketNumber is { } restoredNumber)
        {
            await RecordRoleRestorationAsync(db, observation, restoredNumber, ct);
            return;
        }

        var tracked = observation.Action != TicketAction.Role
            || (observation.RoleId is { } role && config.TrackedRoleIds.Contains(checked((long)role)));

        var continuesTimeout = latest is not null && observation.ContinuesTimeout(latest);
        var captureEnabled = config.Enabled && automodEnabled && config.CaptureFromUtc <= observation.CreatedAtUtc;

        var createsTicket = observation is { CreatesTicket: true, OperationId.HasValue: false }
            && !continuesTimeout
            && tracked
            && captureEnabled;

        Ticket? created = null;

        if (createsTicket)
        {
            created = await CreateAuditTicketAsync(db, observation, resolveReviewPermissionAsync, ct);

            if (older)
                created.End(TicketStatus.Superseded, observation.ModeratorUserId, latest!.Number);
        }

        var canUpdateWithoutOperation = !observation.OperationId.HasValue
            || operation is not null
            || observation.Action is TicketAction.VoiceMute or TicketAction.VoiceDeafen;

        var updateLatest = observation.Action != TicketAction.Kick
            && latest is not null
            && !older
            && latest != operation
            && canUpdateWithoutOperation;

        if (updateLatest)
            await UpdateObservedTicketAsync(latest!, created, observation, operationAudits, tracked, continuesTimeout, resolveReviewPermissionAsync);

        var changed = db.ChangeTracker.HasChanges();
        await db.SaveChangesAsync(ct);

        if (changed)
            reviewSummary.MarkDirty(observation.GuildId);
    }

    private static async Task RecordRoleRestorationAsync(BotDbContext db, TicketObservation observation, long restoredNumber, CancellationToken ct)
    {
        var restored = await db.Tickets
            .SingleOrDefaultAsync(
                ticket => ticket.GuildId == observation.GuildId
                    && ticket.Number == restoredNumber
                    && ticket.Action == TicketAction.Role
                    && ticket.SubjectUserId == observation.SubjectUserId
                    && ticket.RoleId == observation.RoleId,
                ct);

        var newerRestoration = restored is not null && restored.LastAuditId < observation.AuditId;

        if (!newerRestoration)
            return;

        restored!.LastAuditId = observation.AuditId;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Ticket> CreateAuditTicketAsync(
        BotDbContext db,
        TicketObservation observation,
        Func<ulong, Task<bool>> resolveReviewPermissionAsync,
        CancellationToken token)
    {
        var number = await db.Database
            .SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"")
            .SingleAsync(token);

        var details = ReadDetails(observation);

        long? duration = details.HasDuration && details.DurationSeconds != long.MaxValue
            ? details.DurationSeconds
            : null;

        DateTimeOffset? expiry = observation.Timeout.TryGet(out var until)
            ? until
            : null;

        if (expiry is { } end)
            duration = Math.Max(1, (long)Math.Ceiling((end - observation.CreatedAtUtc).TotalSeconds));

        if (observation.Action == TicketAction.Kick)
            duration = null;

        var ticket = new Ticket
        {
            GuildId = observation.GuildId,
            Number = number,
            Action = observation.Action,
            RoleId = observation.RoleId,
            SubjectUserId = observation.SubjectUserId,
            ModeratorUserId = observation.ModeratorUserId,
            CreatedAtUtc = observation.CreatedAtUtc,
            SourceAuditId = observation.AuditId,
            LastAuditId = observation.AuditId,
            AuditReason = observation.Reason,
            ReminderExempt = observation.ModeratorIsBot,
        };

        ticket.Initialise(details.Comment.Apply(string.Empty), duration, expiry);
        var canReview = !observation.RequiresApproval && await resolveReviewPermissionAsync(observation.ModeratorUserId);
        ticket.AcceptCreation(canReview, observation.RequiresApproval);
        db.Tickets.Add(ticket);
        return ticket;
    }

    private static async Task UpdateObservedTicketAsync(
        Ticket latest,
        Ticket? created,
        TicketObservation observation,
        IReadOnlyDictionary<Guid, ulong> operationAudits,
        bool tracked,
        bool continuesTimeout,
        Func<ulong, Task<bool>> resolveReviewPermissionAsync)
    {
        var replacedOutsideCapture = observation.CreatesTicket && (!tracked || !continuesTimeout);
        var hasTimeout = observation.Timeout.TryGet(out var timeout);
        var updatesTimeout = latest.Status == TicketStatus.Active && hasTimeout;

        if (created is not null)
        {
            latest.End(TicketStatus.Superseded, observation.ModeratorUserId, created.Number);
        }
        else if (replacedOutsideCapture)
        {
            latest.End(TicketStatus.Reverted);
        }
        else if (observation.Removed)
        {
            var expired = latest.Action is not (TicketAction.VoiceMute or TicketAction.VoiceDeafen)
                && latest.ExpiresAtUtc <= observation.CreatedAtUtc;

            latest.End(expired ? TicketStatus.Expired : TicketStatus.Reverted, observation.ModeratorUserId);
        }
        else if (updatesTimeout)
        {
            var duration = Math.Max(1, (long)Math.Ceiling((timeout - latest.CreatedAtUtc).TotalSeconds));
            ulong? moderator = observation.ModeratorIsBot ? null : observation.ModeratorUserId;
            var changesDuration = latest.DurationSeconds != duration || latest.ExpiresAtUtc != timeout;
            var canReview = moderator is not null && changesDuration && !observation.RequiresApproval && await resolveReviewPermissionAsync(observation.ModeratorUserId);
            latest.SetDuration(duration, moderator, timeout, canReview, observation.RequiresApproval);
            latest.ObserveTimeoutWindow(timeout, observation.CreatedAtUtc);
        }

        var confirmationFollows = latest.Pending is { } pending
            && operationAudits.TryGetValue(pending.Id, out var audit)
            && audit > observation.AuditId;

        if (!confirmationFollows)
            latest.Pending = null;

        latest.LastAuditId = observation.AuditId;
        latest.Record(TicketHistoryAction.AuditObserved, observation.ModeratorUserId, observation.CreatedAtUtc);
    }

    private static TicketDetails ReadDetails(TicketObservation observation)
    {
        var reason = observation.Reason;

        if (observation.ModeratorIsBot)
            return TicketDetails.ParseFields(duration: null, string.IsNullOrWhiteSpace(reason) ? "No reason attached to the audit log." : reason);

        try
        {
            return string.IsNullOrWhiteSpace(reason)
                ? TicketDetails.ParseFields(duration: null, comment: null)
                : TicketDetails.ParseReply(reason);
        }
        catch (BotCommandException)
        {
            return TicketDetails.ParseFields(duration: null, reason);
        }
    }
}