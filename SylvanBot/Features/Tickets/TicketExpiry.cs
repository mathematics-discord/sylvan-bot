namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RolePersistence;

public sealed class TicketExpiry(DiscordSocketClient client, TicketBoundary boundary, RolePersistenceWorker persistence, ILogger logger)
{
    internal async Task ReconcileBanAsync(BotDbContext db, Ticket ticket, bool auditCurrent, CancellationToken token)
    {
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
            return;

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
            return;

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
                return;

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
                logger.Warning("Scheduled unban rejected for ticket {TicketId} in guild {GuildId} ({HttpCode})",
                    ticket.Id, ticket.GuildId, exception.HttpCode);
                ticket.Pending = null;
                await db.SaveChangesAsync(token);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.Warning(
                    "Ticket {TicketId} in guild {GuildId}: moderation outcome is uncertain ({FailureType})",
                    ticket.Id,
                    ticket.GuildId,
                    exception.GetType().Name);

                return;
            }
        }

        await db.SaveChangesAsync(token);
    }

    public async Task ReconcileRoleAsync(BotDbContext db, Ticket ticket, bool auditCurrent, CancellationToken token)
    {
        if (ticket.SourceAuditId is null)
            ticket.ExpirySuspended = true;

        if (ticket.Status != TicketStatus.Active)
        {
            ticket.Pending = null;

            return;
        }

        if (!auditCurrent || ticket.ExpirySuspended || ticket.ExpiresAtUtc is null || ticket.ExpiresAtUtc > DateTimeOffset.UtcNow)
            return;

        var gate = persistence.Gate(ticket.GuildId);
        await gate.WaitAsync(token);

        try
        {
            var config = await db.TicketConfigurations
               .SingleAsync(value => value.GuildId == ticket.GuildId, token);

            var scan = await boundary.ReadAuditAsync(ticket.GuildId, config.LastAuditScanAtUtc!.Value.AddMinutes(-10), token);

            var newer = scan.Observations
               .Any(value => value.Action == TicketAction.Role
                        && value.SubjectUserId == ticket.SubjectUserId
                        && value.RoleId == ticket.RoleId
                        && value.AuditId > ticket.LastAuditId
                        && (!value.OperationId.HasValue || ticket.Pending == null || value.OperationId.Value != ticket.Pending.Id));

            if (newer)
                return;

            if (ticket.Pending is null)
            {
                ticket.Pending = new TicketOperation { Kind = TicketOperationKind.RemoveRole };
                ticket.Record(TicketHistoryAction.AuditObserved);
            }

            await db.SaveChangesAsync(token);
            var member = await client.Rest.GetGuildUserAsync(ticket.GuildId, ticket.SubjectUserId, TicketBoundary.Options(token));

            if (member?.RoleIds.Contains(ticket.RoleId!.Value) == true)
            {
                if (ticket.Pending.Attempted && ticket.Pending.RequestedAtUtc > DateTimeOffset.UtcNow.AddSeconds(-30))
                    return;

                ticket.Pending.Attempted = true;
                ticket.Pending.RequestedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(token);
                var options = TicketBoundary.Options(token);
                options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {ticket.Pending.Id:D}";

                try
                {
                    await member.RemoveRoleAsync(ticket.RoleId.Value, options);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.Warning("Role expiry pending for ticket {TicketId}: {Failure}", ticket.Id, exception.GetType().Name);

                    return;
                }
            }

            await db.PendingRoles
               .Where(value => value.GuildId == ticket.GuildId && value.UserId == ticket.SubjectUserId && value.RoleId == ticket.RoleId)
               .ExecuteDeleteAsync(token);

            ticket.End(TicketStatus.Expired);
            ticket.Pending = null;
            await db.SaveChangesAsync(token);
        }
        finally
        {
            gate.Release();
        }
    }
}
