using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Tickets;

using Discord.Net;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TimeoutTicketStrategy(DiscordSocketClient client, TicketBoundary boundary, ILogger logger) : ITicketStrategy
{
    public TicketAction Action => TicketAction.Timeout;

    public async Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken cancellationToken)
    {
        if (request is ObserveModeration observed)
            return await BanTicketStrategy.RecordObservedAsync(db, observed, cancellationToken);

        var ticket = request switch
        {
            EditDetails edit => edit.Ticket,
            ReconcileTicket reconcile => reconcile.Ticket,
            _ => throw new ArgumentException("Unsupported timeout request.", nameof(request)),
        };

        var version = ticket.Version;

        switch (request)
        {
            case EditDetails { Details.HasDuration: false }:
                return new TicketResult([], string.Empty);
            case ReconcileTicket { AuditCurrent: false }:
                return new TicketResult([], "Audit reconciliation is unavailable. The timeout request remains pending.");
        }

        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(cancellationToken));
        var member = await guild.GetUserAsync(ticket.SubjectUserId, TicketBoundary.Options(cancellationToken));

        DateTimeOffset? liveExpiry = member?.TimedOutUntil;

        var now = DateTimeOffset.UtcNow;

        if (request is EditDetails editDetails)
        {
            var expiry = editDetails.Details.TimeoutExpiry(ticket.CreatedAtUtc, now);

            if (ticket.Status != TicketStatus.Active || ticket.ExpiresAtUtc <= now)
            {
                if (liveExpiry > now && liveExpiry != ticket.ExpiresAtUtc)
                    throw new BotCommandException("A newer timeout is awaiting audit reconciliation. Nothing was changed.");

                if (ticket.Status == TicketStatus.Active)
                    ticket.End(TicketStatus.Expired);

                ticket.SetDuration(editDetails.Details.DurationSeconds, editDetails.Actor.UserId);

                return new TicketResult([ticket.Id], "Historical timeout details saved.");
            }

            if (member is null || liveExpiry != ticket.ExpiresAtUtc)
                throw new BotCommandException("The live timeout changed. Refresh after audit reconciliation.");

            if (expiry == ticket.ExpiresAtUtc)
                return new TicketResult([], string.Empty);

            ticket.BeginTimeoutChange(new TicketOperation { Kind = "SetTimeoutExpiry", ExpiryUtc = expiry, RequestedBy = editDetails.Actor.UserId });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (ticket.Pending is { } pending)
        {
            if (ticket.Status is TicketStatus.Superseded or TicketStatus.Reverted)
            {
                ticket.Pending = null;
            }
            else if (pending.Command is TicketOperationCommand.ChangeTimeout change)
            {
                var ended = liveExpiry is null || liveExpiry <= now;
                var confirmed = change.ExpiryUtc > now ? liveExpiry == change.ExpiryUtc : ended;

                if (confirmed)
                {
                    Confirm(ticket, change.ExpiryUtc, pending.RequestedBy, now);
                }
                else if (ended)
                {
                    ticket.End(ticket.ExpiresAtUtc <= now ? TicketStatus.Expired : TicketStatus.Reverted);
                    ticket.Pending = null;
                }
                else if (liveExpiry == ticket.ExpiresAtUtc && (!pending.Attempted || pending.RequestedAtUtc <= now.AddSeconds(-30)))
                {
                    var scan = await boundary.ReadAuditAsync(ticket.GuildId, pending.RequestedAtUtc.AddMinutes(-10), cancellationToken);

                    var newer = scan.Observations
                        .Any(value => value.Action == Action
                                      && value.SubjectUserId == ticket.SubjectUserId
                                      && value.AuditId > ticket.LastAuditId
                                      && (!value.OperationId.HasValue || value.OperationId.Value != pending.Id));

                    if (newer)
                        return new TicketResult([ticket.Id], "A newer timeout change is awaiting audit reconciliation.");

                    pending.Attempted = true;
                    await db.SaveChangesAsync(cancellationToken);
                    var options = TicketBoundary.Options(cancellationToken);
                    options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {pending.Id:D}";

                    try
                    {
                        if (change.ExpiryUtc > now)
                            await member!.ModifyAsync(properties => properties.TimedOutUntil = change.ExpiryUtc, options);
                        else
                            await member!.RemoveTimeOutAsync(options);

                        Confirm(ticket, change.ExpiryUtc, pending.RequestedBy, now);
                    }
                    catch (HttpException exception) when (exception.HttpCode is BadRequest or Forbidden or NotFound)
                    {
                        ticket.Pending = null;
                        await db.SaveChangesAsync(cancellationToken);

                        return new TicketResult(
                            [ticket.Id],
                            "Discord rejected the timeout change. Check permissions, hierarchy and member state. The duration was not changed.");
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.Warning(
                            "Ticket {TicketId} in guild {GuildId}: moderation outcome is uncertain ({FailureType})",
                            ticket.Id,
                            ticket.GuildId,
                            exception.GetType().Name);

                        return new TicketResult(
                            [ticket.Id],
                            "The Discord change could not be confirmed. Recovery will reconcile it; do not repeat it blindly.");
                    }
                }
            }
        }
        else if (ticket.Status == TicketStatus.Active && ticket.ExpiresAtUtc <= now && (liveExpiry is null || liveExpiry <= now))
        {
            ticket.End(TicketStatus.Expired);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new TicketResult(
            ticket.Version == version ? [] : [ticket.Id],
            ticket.Pending is null
                ? "Timeout details saved."
                : "The timeout change remains uncertain and will be reconciled.");
    }

    private static void Confirm(Ticket ticket, DateTimeOffset expiry, ulong? requester, DateTimeOffset now)
    {
        ticket.SetDuration(Math.Max(1, (long)Math.Ceiling((expiry - ticket.CreatedAtUtc).TotalSeconds)), requester, expiry);

        if (expiry <= now)
            ticket.End(TicketStatus.Expired, requester);

        ticket.Pending = null;
    }
}