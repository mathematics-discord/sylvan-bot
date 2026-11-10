using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Tickets;

using DotNext;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketTimeoutEnforcement(DiscordSocketClient client, TicketBoundary boundary, ILogger logger)
{
    public Task<TicketResult> EditAsync(BotDbContext db, EditDetails edit, CancellationToken token)
        => HandleAsync(db, edit.Ticket, edit, true, token);

    public Task<TicketResult> ReconcileAsync(BotDbContext db, Ticket ticket, bool auditCurrent, CancellationToken token)
        => HandleAsync(db, ticket, default, auditCurrent, token);

    private async Task<TicketResult> HandleAsync(
        BotDbContext db,
        Ticket ticket,
        Optional<EditDetails> edit,
        bool auditCurrent,
        CancellationToken cancellationToken)
    {
        var version = ticket.Version;
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? recordedExpiry = ticket.AppliedTimeoutUntilUtc;

        if (!edit.HasValue && !auditCurrent)
            return new TicketResult([], "Audit reconciliation is unavailable. Timeout enforcement is paused.");

        if (!edit.HasValue && (ticket.Status != TicketStatus.Active || (ticket.ExpirySuspended && ticket.Pending?.RequestedBy is null)))
        {
            ticket.Pending = null;
            return new TicketResult([], string.Empty);
        }

        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(cancellationToken));
        var member = await guild.GetUserAsync(ticket.SubjectUserId, TicketBoundary.Options(cancellationToken));
        DateTimeOffset? liveExpiry = member?.TimedOutUntil;

        if (edit.TryGet(out var editDetails))
        {
            var expiry = editDetails.Details.TimeoutExpiry(ticket.CreatedAtUtc, now);

            if (ticket.Status != TicketStatus.Active)
            {
                ticket.SetDuration(
                    editDetails.Details.DurationSeconds,
                    editDetails.Actor.UserId,
                    canReview: editDetails.Actor.IsAdministrator || editDetails.Actor.CanViewAuditLog);

                return new TicketResult([ticket.Id], "Historical timeout details saved.");
            }

            if (member is null)
                throw new BotCommandException("This member is no longer in the server. The timeout was not changed.");

            if (expiry == ticket.ExpiresAtUtc && !ticket.ExpirySuspended)
                return new TicketResult([], string.Empty);

            // An authorised edit sets the current timeout, including restrictions
            // without a recorded native window. Retain this observation for uncertain retries.
            logger.Information(
                "Applying moderator duration edit to timeout ticket {TicketId}; stored native expiry {StoredExpiry}, live expiry {LiveExpiry}, requested expiry {RequestedExpiry}, enforcement suspended {ExpirySuspended}",
                ticket.Id,
                ticket.AppliedTimeoutUntilUtc,
                liveExpiry,
                expiry,
                ticket.ExpirySuspended);

            ticket.ObserveTimeoutWindow(liveExpiry, now);

            ticket.BeginTimeoutChange(
                new TicketOperation
                {
                    Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = expiry,
                    NativeExpiryUtc = Ticket.NativeTimeoutExpiry(expiry, now), RequestedBy = editDetails.Actor.UserId,
                });

            await db.SaveChangesAsync(cancellationToken);
        }

        if (ticket.Pending is null)
        {
            ticket.TimeoutCheckAtUtc = now.AddDays(1);

            if (ticket.ExpiresAtUtc <= now && (liveExpiry is null || liveExpiry <= now))
                ticket.End(TicketStatus.Expired);

            if (member is not null && ticket.Status == TicketStatus.Active)
            {
                // An unexplained live change must be captured before we overwrite it.
                var unexpected = liveExpiry > now && liveExpiry != ticket.AppliedTimeoutUntilUtc;
                var clearedEarly = liveExpiry is null && ticket.AppliedTimeoutUntilUtc > now;
                var rejoined = member.JoinedAt > ticket.CreatedAtUtc;

                if (unexpected || (clearedEarly && !rejoined))
                {
                    ticket.TimeoutCheckAtUtc = now.AddSeconds(30);
                    await db.SaveChangesAsync(cancellationToken);

                    return new TicketResult([], "A live timeout change is awaiting audit reconciliation.");
                }

                if (ticket.NeedsTimeoutRenewal(now) || liveExpiry is null || liveExpiry <= now)
                {
                    ticket.BeginTimeoutChange(
                        new TicketOperation
                        {
                            Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = ticket.ExpiresAtUtc,
                            NativeExpiryUtc = Ticket.NativeTimeoutExpiry(ticket.ExpiresAtUtc!.Value, now),
                        });

                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        if (ticket.Pending is { } pending)
        {
            var expiry = pending.ExpiryUtc!.Value;
            var nativeExpiry = pending.NativeExpiryUtc ?? expiry;
            var ended = liveExpiry is null || liveExpiry <= now;
            var confirmed = expiry > now ? member is not null && liveExpiry == nativeExpiry : ended;

            if (confirmed)
            {
                ticket.ConfirmTimeoutChange(
                    now,
                    pending.RequestedBy is { } requester
                    && await boundary.CanReviewAsync(ticket.GuildId, requester, cancellationToken));
            }
            else if (member is null)
            {
                // Absence does not cancel a durable timeout or its uncertain operation.
                ticket.TimeoutCheckAtUtc = now.AddDays(1);
            }
            else if (!pending.Attempted || pending.RequestedAtUtc <= now.AddSeconds(-30))
            {
                // Initial moderator edits express a new decision. Only automatic work and
                // uncertain retries must establish that no later Discord action superseded it.
                if (!edit.HasValue)
                {
                    var configuration = await db.TicketConfigurations
                       .SingleAsync(value => value.GuildId == ticket.GuildId, cancellationToken);

                    var from = configuration.LastAuditScanAtUtc ?? configuration.CaptureFromUtc!.Value;

                    if (from < now.AddDays(-45))
                    {
                        ticket.ExpirySuspended = true;
                        ticket.Pending = null;
                        await db.SaveChangesAsync(cancellationToken);

                        return new TicketResult([ticket.Id], "Timeout enforcement is suspended because audit history is incomplete.");
                    }

                    var scan = await boundary.ReadAuditAsync(ticket.GuildId, from.AddMinutes(-10), cancellationToken);

                    var newer = scan.Observations
                       .Any(value => value.Action == TicketAction.Timeout
                                && value.SubjectUserId == ticket.SubjectUserId
                                && value.AuditId > ticket.LastAuditId
                                && (!value.OperationId.HasValue || value.OperationId.Value != pending.Id));

                    if (newer)
                        return new TicketResult([ticket.Id], "A newer timeout change is awaiting audit reconciliation.");

                    var unexpected = liveExpiry > now && liveExpiry != ticket.AppliedTimeoutUntilUtc;

                    if (unexpected)
                        return new TicketResult([ticket.Id], "The live timeout changed. Recovery is awaiting audit reconciliation.");
                }

                // A retry after an expired native window needs a fresh, persisted target.
                if (expiry > now && nativeExpiry <= now)
                {
                    nativeExpiry = Ticket.NativeTimeoutExpiry(expiry, now);
                    pending.NativeExpiryUtc = nativeExpiry;
                }

                pending.Attempted = true;
                pending.RequestedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                var options = TicketBoundary.Options(cancellationToken);
                options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {pending.Id:D}";

                try
                {
                    if (expiry > now)
                        await member.ModifyAsync(properties => properties.TimedOutUntil = nativeExpiry, options);
                    else
                        await member.RemoveTimeOutAsync(options);

                    ticket.ConfirmTimeoutChange(
                        now,
                        pending.RequestedBy is { } requester
                        && await boundary.CanReviewAsync(ticket.GuildId, requester, cancellationToken));
                }
                catch (HttpException exception) when (exception.HttpCode is BadRequest or Forbidden or NotFound)
                {
                    logger.Warning(
                        "Timeout enforcement rejected for ticket {TicketId} in guild {GuildId}, member {MemberId} ({HttpCode})",
                        ticket.Id,
                        ticket.GuildId,
                        ticket.SubjectUserId,
                        exception.HttpCode);

                    ticket.Pending = null;
                    ticket.ObserveTimeoutWindow(recordedExpiry, now);
                    ticket.TimeoutCheckAtUtc = now.AddMinutes(5);
                    await db.SaveChangesAsync(cancellationToken);

                    return new TicketResult(
                        [ticket.Id],
                        "Discord rejected the timeout change. Check permissions, hierarchy and member state. The duration was not changed.");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.Warning(
                        "Timeout outcome is uncertain for ticket {TicketId} in guild {GuildId}, member {MemberId} ({FailureType})",
                        ticket.Id,
                        ticket.GuildId,
                        ticket.SubjectUserId,
                        exception.GetType().Name);

                    return new TicketResult(
                        [ticket.Id],
                        "The Discord change could not be confirmed. Recovery will reconcile it; do not repeat it blindly.");
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new TicketResult(
            ticket.Version == version ? [] : [ticket.Id],
            ticket.Pending is null ? "Timeout details saved." : "The timeout change remains pending and will be reconciled.");
    }
}