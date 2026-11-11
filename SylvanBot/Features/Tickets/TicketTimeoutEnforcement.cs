using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketTimeoutEnforcement(DiscordSocketClient client, TicketBoundary boundary, ILogger logger)
{
    public async Task<string> EditAsync(
        BotDbContext db,
        Ticket ticket,
        TicketActor actor,
        TicketDetails details,
        CancellationToken cancellationToken)
    {
        if (ticket.Status != TicketStatus.Active)
        {
            ticket.SetDuration(
                details.DurationSeconds,
                actor.UserId,
                canReview: actor.IsAdministrator || actor.CanViewAuditLog);

            return "Historical timeout details saved.";
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? recordedExpiry = ticket.AppliedTimeoutUntilUtc;
        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(cancellationToken));
        var member = await guild.GetUserAsync(ticket.SubjectUserId, TicketBoundary.Options(cancellationToken));
        DateTimeOffset? liveExpiry = member?.TimedOutUntil;

        var expiry = details.TimeoutExpiry(ticket.CreatedAtUtc);

        if (member is null)
            throw new BotCommandException("This member is no longer in the server. The timeout was not changed.");

        if (expiry == ticket.ExpiresAtUtc && !ticket.ExpirySuspended)
            return string.Empty;

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
                Kind = TicketOperationKind.SetTimeoutExpiry,
                ExpiryUtc = expiry,
                NativeExpiryUtc = Ticket.NativeTimeoutExpiry(expiry, now),
                RequestedBy = actor.UserId,
            });

        await db.SaveChangesAsync(cancellationToken);

        return await ApplyPendingAsync(db, ticket, member, recordedExpiry, now, actor, cancellationToken);
    }

    public async Task ReconcileAsync(BotDbContext db, Ticket ticket, bool auditCurrent, CancellationToken cancellationToken)
    {
        if (!auditCurrent)
            return;

        var enforcementStopped = ticket.Status != TicketStatus.Active
            || (ticket.ExpirySuspended && ticket.Pending?.RequestedBy is null);

        if (enforcementStopped)
        {
            ticket.Pending = null;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? recordedExpiry = ticket.AppliedTimeoutUntilUtc;
        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(cancellationToken));
        var member = await guild.GetUserAsync(ticket.SubjectUserId, TicketBoundary.Options(cancellationToken));
        DateTimeOffset? liveExpiry = member?.TimedOutUntil;

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

                    return;
                }

                if (ticket.NeedsTimeoutRenewal(now) || liveExpiry is null || liveExpiry <= now)
                {
                    ticket.BeginTimeoutChange(
                        new TicketOperation
                        {
                            Kind = TicketOperationKind.SetTimeoutExpiry,
                            ExpiryUtc = ticket.ExpiresAtUtc,
                            NativeExpiryUtc = Ticket.NativeTimeoutExpiry(ticket.ExpiresAtUtc!.Value, now),
                        });

                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        await ApplyPendingAsync(db, ticket, member, recordedExpiry, now, actor: null, cancellationToken);
    }

    private async Task<string> ApplyPendingAsync(
        BotDbContext db,
        Ticket ticket,
        IGuildUser? member,
        DateTimeOffset? recordedExpiry,
        DateTimeOffset now,
        TicketActor? actor,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? liveExpiry = member?.TimedOutUntil;

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
                    await RequesterCanReviewAsync(ticket.GuildId, pending, actor, cancellationToken));
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
                if (actor is null)
                {
                    var configuration = await db.TicketConfigurations
                        .SingleAsync(value => value.GuildId == ticket.GuildId, cancellationToken);

                    var from = configuration.LastAuditScanAtUtc ?? configuration.CaptureFromUtc!.Value;

                    if (from < now.AddDays(-45))
                    {
                        ticket.ExpirySuspended = true;
                        ticket.Pending = null;
                        await db.SaveChangesAsync(cancellationToken);

                        logger.Warning(
                            "Timeout enforcement suspended for ticket {TicketId} in guild {GuildId}: audit history is incomplete",
                            ticket.Id,
                            ticket.GuildId);

                        return string.Empty;
                    }

                    var scan = await boundary.ReadAuditAsync(ticket.GuildId, from.AddMinutes(-10), cancellationToken);

                    var newer = scan.Observations
                        .Any(value => value.Action == TicketAction.Timeout
                                 && value.SubjectUserId == ticket.SubjectUserId
                                 && value.AuditId > ticket.LastAuditId
                                 && (!value.OperationId.HasValue || value.OperationId.Value != pending.Id));

                    if (newer)
                        return string.Empty;

                    var unexpected = liveExpiry > now && liveExpiry != ticket.AppliedTimeoutUntilUtc;

                    if (unexpected)
                        return string.Empty;
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
                        await RequesterCanReviewAsync(ticket.GuildId, pending, actor, cancellationToken));
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

                    return "Discord rejected the timeout change. Check permissions, hierarchy, and member state. The duration was not changed.";
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.Warning(
                        "Timeout outcome is uncertain for ticket {TicketId} in guild {GuildId}, member {MemberId} ({FailureType})",
                        ticket.Id,
                        ticket.GuildId,
                        ticket.SubjectUserId,
                        exception.GetType().Name);

                    return "The Discord change could not be confirmed. Recovery will reconcile it; do not repeat it blindly.";
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return ticket.Pending is null ? "Timeout details saved." : "The timeout change remains pending and will be reconciled.";
    }

    private async Task<bool> RequesterCanReviewAsync(
        ulong guildId,
        TicketOperation pending,
        TicketActor? actor,
        CancellationToken token)
    {
        if (pending.RequestedBy is not { } requester)
            return false;

        if (actor is not null && actor.UserId == requester)
            return actor.IsAdministrator || actor.CanViewAuditLog;

        return await boundary.CanReviewAsync(guildId, requester, token);
    }
}