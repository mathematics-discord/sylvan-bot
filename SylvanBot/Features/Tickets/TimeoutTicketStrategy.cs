using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TimeoutTicketStrategy(DiscordSocketClient client, TicketBoundary boundary, ILogger logger) : ITicketStrategy
{
    public TicketAction Action => TicketAction.Timeout;

    public async Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken cancellationToken)
    {
        if (request.Value is ObserveModeration observed)
            return await BanTicketStrategy.RecordObservedAsync(db, observed, cancellationToken);

        var ticket = request.Value switch
        {
            EditDetails edit => edit.Ticket,
            ReconcileTicket reconcile => reconcile.Ticket,
            _ => throw new ArgumentException("Unsupported timeout request.", nameof(request)),
        };

        var version = ticket.Version;
        var now = DateTimeOffset.UtcNow;

        switch (request.Value)
        {
            case EditDetails { Details.HasDuration: false }:
                return new TicketResult([], string.Empty);
            case ReconcileTicket { AuditCurrent: false }:
                return new TicketResult([], "Audit reconciliation is unavailable. Timeout enforcement is paused for this pass.");
            case ReconcileTicket when (ticket.Status != TicketStatus.Active || ticket.ExpirySuspended):
                ticket.Pending = null;

                return new TicketResult([], string.Empty);
        }

        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(cancellationToken));
        var member = await guild.GetUserAsync(ticket.SubjectUserId, TicketBoundary.Options(cancellationToken));
        DateTimeOffset? liveExpiry = member?.TimedOutUntil;

        if (request.Value is EditDetails editDetails)
        {
            var expiry = editDetails.Details.TimeoutExpiry(ticket.CreatedAtUtc, now);

            if (ticket.Status != TicketStatus.Active || ticket.ExpiresAtUtc <= now)
            {
                if (liveExpiry > now && liveExpiry != ticket.AppliedTimeoutUntilUtc)
                    throw new BotCommandException("A newer timeout is awaiting audit reconciliation. Nothing was changed.");

                if (ticket.Status == TicketStatus.Active)
                    ticket.End(TicketStatus.Expired);

                ticket.SetDuration(editDetails.Details.DurationSeconds, editDetails.Actor.UserId);

                return new TicketResult([ticket.Id], "Historical timeout details saved.");
            }

            var liveEnded = liveExpiry is null || liveExpiry <= now;

            var expectedLive = liveExpiry == ticket.AppliedTimeoutUntilUtc
                || (liveEnded && (ticket.AppliedTimeoutUntilUtc <= now || ticket.ExpirySuspended));

            if (member is null || !expectedLive)
                throw new BotCommandException("The live timeout changed. Refresh after audit reconciliation.");

            if (expiry == ticket.ExpiresAtUtc && !ticket.ExpirySuspended)
                return new TicketResult([], string.Empty);

            ticket.BeginTimeoutChange(
                new TicketOperation
                {
                    Kind = "SetTimeoutExpiry", ExpiryUtc = expiry,
                    NativeExpiryUtc = Ticket.NativeTimeoutExpiry(expiry, now), RequestedBy = editDetails.Actor.UserId,
                });

            await db.SaveChangesAsync(cancellationToken);
        }

        if (ticket.Pending is null)
        {
            ticket.TimeoutCheckAtUtc = now.AddDays(1);

            if (ticket.ExpiresAtUtc <= now && (liveExpiry is null || liveExpiry <= now))
                ticket.End(TicketStatus.Expired);

            if (member is not null)
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
                            Kind = "SetTimeoutExpiry", ExpiryUtc = ticket.ExpiresAtUtc,
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
                ticket.ConfirmTimeoutChange(now);
            }
            else if (member is null)
            {
                // Absence does not cancel a durable timeout or its uncertain operation.
                ticket.TimeoutCheckAtUtc = now.AddDays(1);
            }
            else if (!pending.Attempted || pending.RequestedAtUtc <= now.AddSeconds(-30))
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
                   .Any(value => value.Action == Action
                            && value.SubjectUserId == ticket.SubjectUserId
                            && value.AuditId > ticket.LastAuditId
                            && (!value.OperationId.HasValue || value.OperationId.Value != pending.Id));

                if (newer)
                    return new TicketResult([ticket.Id], "A newer timeout change is awaiting audit reconciliation.");

                var unexpected = liveExpiry > now && liveExpiry != ticket.AppliedTimeoutUntilUtc;

                if (unexpected)
                    return new TicketResult([ticket.Id], "The live timeout changed. Recovery is awaiting audit reconciliation.");

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

                    ticket.ConfirmTimeoutChange(now);
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