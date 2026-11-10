namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketBanEnforcement(DiscordSocketClient client, TicketBoundary boundary, ILogger logger)
{
    internal async Task<TicketResult> ReconcileAsync(BotDbContext db, Ticket ticket, bool auditCurrent, CancellationToken token)
    {
        var version = ticket.Version;
        var now = DateTimeOffset.UtcNow;

        if (ticket.SourceAuditId is null)
            ticket.ExpirySuspended = true;

        if (ticket.Status != TicketStatus.Active)
            ticket.Pending = null;

        if (!auditCurrent
            || ticket.ExpirySuspended
            || ticket.Status != TicketStatus.Active
            || ticket.ExpiresAtUtc is null
            || ticket.ExpiresAtUtc > now)
            return new TicketResult([], string.Empty);

        // Recheck immediately before unban. A new audit entry must be ingested first.
        var configuration = await db.TicketConfigurations
            .SingleAsync(value => value.GuildId == ticket.GuildId, token);
        var from = configuration.LastAuditScanAtUtc!.Value;
        var scan = await boundary.ReadAuditAsync(ticket.GuildId, from.AddMinutes(-10), token);

        var changed = scan.Observations
           .Any(value => value.Action == TicketAction.Ban
                    && value.SubjectUserId == ticket.SubjectUserId
                    && value.AuditId > ticket.LastAuditId
                    && (!value.OperationId.HasValue || ticket.Pending is null || value.OperationId.Value != ticket.Pending.Id));

        if (changed)
            return new TicketResult([], "A newer ban change is awaiting audit reconciliation.");

        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(token));
        var ban = await guild.GetBanAsync(ticket.SubjectUserId, TicketBoundary.Options(token));

        if (ban is null)
        {
            ticket.End(ticket.Pending is null ? TicketStatus.Reverted : TicketStatus.Expired);
            ticket.Pending = null;
        }
        else
        {
            if (ticket.Pending is { Attempted: true } pending && pending.RequestedAtUtc > now.AddSeconds(-30))
                return new TicketResult([], "The scheduled unban is awaiting confirmation.");

            ticket.Pending ??= new TicketOperation { Kind = TicketOperationKind.RemoveBan };
            await db.SaveChangesAsync(token);
            ticket.Pending.Attempted = true;
            await db.SaveChangesAsync(token);
            var options = TicketBoundary.Options(token);
            options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {ticket.Pending.Id:D}";

            try
            {
                await guild.RemoveBanAsync(ticket.SubjectUserId, options);
                ticket.End(TicketStatus.Expired);
                ticket.Pending = null;
            }
            catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden)
            {
                // A definitive rejection leaves the restriction intact and the expiry retryable.
                ticket.Pending = null;
                await db.SaveChangesAsync(token);

                return new TicketResult([ticket.Id], "Discord rejected the scheduled unban. Check Sylvan's Ban Members permission.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.Warning(
                    "Ticket {TicketId} in guild {GuildId}: moderation outcome is uncertain ({FailureType})",
                    ticket.Id,
                    ticket.GuildId,
                    exception.GetType().Name);

                return new TicketResult([ticket.Id], "The unban could not be confirmed. Recovery will reconcile it; do not repeat it blindly.");
            }
        }

        await db.SaveChangesAsync(token);

        return new TicketResult(ticket.Version == version ? [] : [ticket.Id], "Ban expiry reconciled.");
    }
}
