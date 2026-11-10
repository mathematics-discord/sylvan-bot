using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.Tickets;

internal static class TicketAuditWorkflow
{
    internal static async Task RecordAsync(BotDbContext db, ObserveModeration request, CancellationToken token, TicketBoundary boundary)
    {
        var observation = request.Observation;

        var alreadyRecorded = await db.Tickets
           .AnyAsync(
                ticket => ticket.GuildId == observation.GuildId
                    && ticket.SourceAuditId == observation.AuditId
                    && ticket.Action == observation.Action
                    && ticket.RoleId == observation.RoleId,
                token);

        if (alreadyRecorded)
            return;

        var latest = await db.Tickets
           .Where(ticket => ticket.GuildId == observation.GuildId
                      && ticket.SubjectUserId == observation.SubjectUserId
                      && ticket.Action == observation.Action
                      && ticket.RoleId == observation.RoleId
                      && ticket.SourceAuditId != null)
           .OrderByDescending(ticket => ticket.SourceAuditId)
           .FirstOrDefaultAsync(token);

        var operation = observation.OperationId.HasValue
            ? await db.Tickets
               .SingleOrDefaultAsync(
                    ticket => ticket.GuildId == observation.GuildId
                        && ticket.SubjectUserId == observation.SubjectUserId
                        && ticket.Action == observation.Action
                        && ticket.RoleId == observation.RoleId
                        && ticket.Pending != null
                        && ticket.Pending.Id == observation.OperationId.Value,
                    token)
            : null;

        var older = observation.Action != TicketAction.Kick && latest is not null && latest.LastAuditId >= observation.AuditId;

        // A continuation has no source row of its own. Replaying it must not create a
        // historical ticket after the logical restriction subsequently ended.
        if (observation.Action == TicketAction.Timeout && older && operation is null)
            return;

        if (observation is { Action: TicketAction.Timeout, OperationId.HasValue: true } && operation is null)
        {
            if (latest is null || older)
                return;

            latest.LastAuditId = observation.AuditId;
            await db.SaveChangesAsync(token);

            return;
        }

        if (operation is not null && operation.LastAuditId < observation.AuditId)
        {
            if (operation.Pending!.Kind == TicketOperationKind.SetTimeoutExpiry && operation.Pending.ExpiryUtc is { } timeoutExpiry)
            {
                var nativeExpiry = operation.Pending.NativeExpiryUtc ?? timeoutExpiry;

                var matches = timeoutExpiry <= observation.CreatedAtUtc
                    ? observation.Removed
                    : observation.Timeout.Value is TicketTimeout.Until applied && applied.ExpiresAtUtc == nativeExpiry;

                if (!matches || operation.Status != TicketStatus.Active
                    || (operation.ExpirySuspended && operation.Pending.RequestedBy is null))
                    return;

                operation.ConfirmTimeoutChange(observation.CreatedAtUtc, operation.Pending.RequestedBy is { } requester
                    && await boundary.CanReviewAsync(operation.GuildId, requester, token));
            }

            if (observation.Removed && operation.Status != TicketStatus.Superseded)
                operation.End(operation.ExpiresAtUtc <= observation.CreatedAtUtc ? TicketStatus.Expired : TicketStatus.Reverted);

            operation.Pending = null;
            operation.LastAuditId = observation.AuditId;
            operation.Record(TicketHistoryAction.AuditObserved, observation.ModeratorUserId, observation.CreatedAtUtc);
        }

        var config = await db.TicketConfigurations
           .SingleAsync(value => value.GuildId == observation.GuildId, token);

        if (observation.RestoredTicketNumber is { } restoredNumber)
        {
            var restored = await db.Tickets
               .SingleOrDefaultAsync(
                    ticket => ticket.GuildId == observation.GuildId
                        && ticket.Number == restoredNumber
                        && ticket.Action == TicketAction.Role
                        && ticket.SubjectUserId == observation.SubjectUserId
                        && ticket.RoleId == observation.RoleId,
                    token);

            if (restored is not null && restored.LastAuditId < observation.AuditId)
            {
                restored.LastAuditId = observation.AuditId;
                await db.SaveChangesAsync(token);
            }

            return;
        }

        var tracked = observation.Action != TicketAction.Role
            || (observation.RoleId is { } role && config.TrackedRoleIds.Contains(checked((long)role)));

        Ticket? created = null;

        var continuesTimeout = latest is not null && observation.ContinuesTimeout(latest);

        if (observation is { CreatesTicket: true, OperationId.HasValue: false } && !continuesTimeout && tracked && config.Enabled && request.CaptureEnabled && config.CaptureFromUtc <= observation.CreatedAtUtc)
        {
            var number = await db.Database
               .SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"")
               .SingleAsync(token);

            var details = ReadDetails(observation);

            long? duration = details.HasDuration && details.DurationSeconds != long.MaxValue
                ? details.DurationSeconds
                : null;

            DateTimeOffset? expiry = observation.Timeout.Value is TicketTimeout.Until until
                ? until.ExpiresAtUtc
                : null;

            if (expiry is { } end)
                duration = Math.Max(1, (long)Math.Ceiling((end - observation.CreatedAtUtc).TotalSeconds));

            if (observation.Action == TicketAction.Kick)
                duration = null;

            created = new Ticket
            {
                GuildId = observation.GuildId, Number = number, Action = observation.Action, RoleId = observation.RoleId,
                SubjectUserId = observation.SubjectUserId, ModeratorUserId = observation.ModeratorUserId,
                CreatedAtUtc = observation.CreatedAtUtc, SourceAuditId = observation.AuditId, LastAuditId = observation.AuditId,
                AuditReason = observation.Reason, ReminderExempt = observation.ModeratorIsBot,
            };

            created.Initialise(details.Comment.Apply(string.Empty), duration, expiry);
            created.AcceptCreation(request.CanReview);

            if (older)
                created.End(TicketStatus.Superseded, observation.ModeratorUserId, latest!.Number);

            db.Tickets.Add(created);
        }

        var updateLatest = observation.Action != TicketAction.Kick
            && latest is not null
            && !older
            && latest != operation
            && (!observation.OperationId.HasValue || operation is not null
                || observation.Action is TicketAction.VoiceMute or TicketAction.VoiceDeafen);

        if (updateLatest)
        {
            if (created is not null)
            {
                latest!.End(TicketStatus.Superseded, observation.ModeratorUserId, created.Number);
            }
            else if (observation.CreatesTicket && (!tracked || !continuesTimeout))
            {
                // A replacement that was not captured must still end our ownership.
                latest!.End(TicketStatus.Reverted);
            }
            else if (observation.Removed)
            {
                latest!.End(
                    latest.Action is not (TicketAction.VoiceMute or TicketAction.VoiceDeafen)
                        && latest.ExpiresAtUtc <= observation.CreatedAtUtc ? TicketStatus.Expired : TicketStatus.Reverted,
                    observation.ModeratorUserId);
            }
            else if (observation.Timeout.Value is TicketTimeout.Until timeout && latest!.Status == TicketStatus.Active)
            {
                latest.SetDuration(
                    Math.Max(1, (long)Math.Ceiling((timeout.ExpiresAtUtc - latest.CreatedAtUtc).TotalSeconds)),
                    observation.ModeratorIsBot ? null : observation.ModeratorUserId,
                    timeout.ExpiresAtUtc, request.CanReview);

                latest.ObserveTimeoutWindow(timeout.ExpiresAtUtc, observation.CreatedAtUtc);
            }

            var confirmationFollows = latest!.Pending is { } pending
                && request.OperationAudits.TryGetValue(pending.Id, out var audit)
                && audit > observation.AuditId;

            if (!confirmationFollows)
                latest.Pending = null;

            latest.LastAuditId = observation.AuditId;
            latest.Record(TicketHistoryAction.AuditObserved, observation.ModeratorUserId, observation.CreatedAtUtc);
        }

        await db.SaveChangesAsync(token);


    }

    private static TicketDetails ReadDetails(TicketObservation observation)
    {
        var reason = observation.Reason;

        if (observation.ModeratorIsBot)
            return TicketDetails.ParseFields(null, string.IsNullOrWhiteSpace(reason) ? "No reason attached to the audit log." : reason);

        try
        {
            return string.IsNullOrWhiteSpace(reason)
                ? TicketDetails.ParseFields(null, null)
                : TicketDetails.ParseReply(reason);
        }
        catch (BotCommandException)
        {
            return TicketDetails.ParseFields(null, reason);
        }
    }

}
