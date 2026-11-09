using static System.StringComparison;

namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed partial class TicketCoordinator
{
    // The issuance row retains the original rule, message, reason and requested deadline.
    // Once correlated, the ticket alone controls changes and expiry.
    internal async Task RecoverAutomodBansAsync(CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var now = DateTimeOffset.UtcNow;

        List<Guid> due = await db.AutomodBans.AsNoTracking()
           .Where(op => op.NextCheckAt <= now
                      && !db.TicketConfigurations.Any(c => c.GuildId == op.GuildId && c.OrderedPublicationPending && !c.Enabled)
                      && (op.Status == "Active"
                          || op.Status == "Pending"
                          || op.Status == "Removing"
                          || (op.Status == "TicketOwned"
                              && !db.TicketConfigurations.Any(c => c.GuildId == op.GuildId && c.Enabled)
                              && db.Tickets.Any(t => t.GuildId == op.GuildId
                                                    && t.SourceAuditId == op.AuditId
                                                    && t.Action == TicketAction.Ban
                                                    && t.Status == TicketStatus.Active
                                                    && !t.ExpirySuspended))))
           .OrderBy(op => op.NextCheckAt)
           .Select(op => op.Id)
           .Take(100)
           .ToListAsync(token);

        foreach (var id in due)
        {
            try
            {
                await using var work = await contexts.CreateDbContextAsync(token);
                await ReconcileAutomodBanAsync(work, id, token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.Warning("Automod ban {OperationId} awaits ticket reconciliation ({FailureType})", id, e.GetType().Name);
            }
        }
    }

    private async Task ReconcileAutomodBanAsync(BotDbContext db, Guid id, CancellationToken token)
    {
        var op = await db.AutomodBans.SingleAsync(value => value.Id == id, token);
        var config = await db.TicketConfigurations.SingleOrDefaultAsync(value => value.GuildId == op.GuildId, token);
        var now = DateTimeOffset.UtcNow;
        var from = op.CheckedAt;

        if (string.Equals(op.Status, "TicketOwned", Ordinal) && config?.LastAuditScanAtUtc is { } scanned && scanned > from)
            from = scanned;

        if (now - from >= TimeSpan.FromDays(45))
        {
            op.Status = "Suspended";

            var uncertainId = await db.Tickets
                .Where(ticket => ticket.GuildId == op.GuildId && ticket.SourceAuditId == op.AuditId && ticket.Action == TicketAction.Ban)
                .Select(ticket => (long?)ticket.Id)
                .SingleOrDefaultAsync(token);

            using var gate = uncertainId is { } ticketId ? await locks.TicketAsync(ticketId, token) : null;

            var uncertain = await db.Tickets.SingleOrDefaultAsync(
                t => t.GuildId == op.GuildId && t.SourceAuditId == op.AuditId && t.Action == TicketAction.Ban,
                token);

            if (uncertain is { Status: TicketStatus.Active })
                uncertain.ExpirySuspended = true;

            await db.SaveChangesAsync(token);
            logger.Warning("Automod ban {OperationId} has an audit gap and requires review", id);
            return;
        }

        var scan = await boundary.ReadAuditAsync(op.GuildId, from.AddMinutes(-10), token);
        TicketObservation[] changes = scan.Observations
           .Where(o => o.Action == TicketAction.Ban && o.SubjectUserId == op.UserId)
           .ToArray();

        if (op.AuditId == 0)
        {
            TicketObservation[] matches = changes
               .Where(o => !o.Removed
                                            && o.ModeratorUserId == client.CurrentUser.Id
                                            && o.CreatedAtUtc >= op.RequestedAt.AddSeconds(-1)
                                            && o.CreatedAtUtc <= op.RequestedAt.AddMinutes(5)
                                            && string.Equals(o.Reason, op.Reason, Ordinal))
               .ToArray();

            if (matches.Length != 1)
            {
                if (matches.Length > 1 || now - op.RequestedAt > TimeSpan.FromMinutes(15))
                {
                    op.Status = "Suspended";
                    logger.Warning("Automod ban {OperationId} has no unique matching ban audit and requires review", id);
                }

                op.NextCheckAt = now.AddSeconds(30);
                await db.SaveChangesAsync(token);
                return;
            }

            op.AuditId = matches[0].AuditId;
        }

        IQueryable<long> priorId = db.Tickets
            .Where(ticket => ticket.GuildId == op.GuildId && ticket.SubjectUserId == op.UserId
                && ticket.Action == TicketAction.Ban && ticket.Status == TicketStatus.Active && ticket.SourceAuditId != null)
            .OrderByDescending(ticket => ticket.SourceAuditId)
            .Select(ticket => ticket.Id)
            .Take(1);

        List<long> existing = await db.Tickets
            .Where(ticket => ticket.GuildId == op.GuildId && ticket.Action == TicketAction.Ban
                && (ticket.SourceAuditId == op.AuditId || priorId.Contains(ticket.Id)))
            .Select(ticket => ticket.Id)
            .ToListAsync(token);

        using var ticketGates = await locks.TicketsAsync(existing, token);

        var ticket = await db.Tickets.SingleOrDefaultAsync(
            t => t.GuildId == op.GuildId && t.Action == TicketAction.Ban && t.SourceAuditId == op.AuditId,
            token);

        var firstTransfer = op.Status != "TicketOwned";
        var created = ticket is null;

        if (ticket is null)
        {
            if (!firstTransfer)
                throw new InvalidOperationException("The correlated ban ticket is missing.");

            var guild = client.GetGuild(op.GuildId) ?? throw new InvalidOperationException("Guild unavailable.");
            var number = await db.Database.SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"").SingleAsync(token);

            var exempt = config is not null
                && (config.ReviewExemptRoleIds.Contains((long)guild.Id)
                    || guild.CurrentUser.Roles.Any(r => config.ReviewExemptRoleIds.Contains((long)r.Id)));

            ticket = new Ticket
            {
                GuildId = op.GuildId, SubjectUserId = op.UserId, ModeratorUserId = client.CurrentUser.Id,
                Number = number, Action = TicketAction.Ban, CreatedAtUtc = SnowflakeUtils.FromSnowflake(op.AuditId),
                ReviewExempt = exempt, ReminderExempt = true, SourceAuditId = op.AuditId, LastAuditId = op.AuditId,
                AuditReason = op.Reason,
            };

            ticket.Initialise(op.Reason, null);

            var prior = await db.Tickets.Where(t => t.GuildId == op.GuildId
                                                   && t.SubjectUserId == op.UserId
                                                   && t.Action == TicketAction.Ban
                                                   && t.Status == TicketStatus.Active
                                                   && t.SourceAuditId != null)
               .OrderByDescending(t => t.SourceAuditId)
               .FirstOrDefaultAsync(token);

            if (prior is not null)
            {
                if (prior.LastAuditId >= op.AuditId)
                    ticket.End(TicketStatus.Superseded, successor: prior.Number);
                else
                {
                    prior.End(TicketStatus.Superseded, successor: ticket.Number);
                    prior.Pending = null;
                }
            }

            db.Tickets.Add(ticket);
        }

        if (ticket.SubjectUserId != op.UserId)
            throw new InvalidOperationException("The correlated audit belongs to another subject.");

        if (firstTransfer
            && ticket.Status == TicketStatus.Active
            && ticket.DurationSeconds is null
            && !ticket.ModificationHistory.Any(h => h.Action == TicketHistoryAction.SetDuration)
            && op.ExpiresAtUtc is { } deadline)
        {
            ticket.SetDuration(Math.Max(1, (long)Math.Ceiling((deadline - ticket.CreatedAtUtc).TotalSeconds)), null, deadline);
        }

        // Even with public capture disabled, a later unban or replacement ban ends this ownership.
        var later = changes.FirstOrDefault(o => o.AuditId > ticket.LastAuditId);

        if (later is not null && ticket.Status == TicketStatus.Active)
        {
            var ours = ticket.Pending is { } p && later.OperationId.TryGet(out var tag) && tag == p.Id;
            ticket.End(ours && later.Removed ? TicketStatus.Expired : TicketStatus.Reverted);
            ticket.Pending = null;
            ticket.LastAuditId = later.AuditId;
        }

        op.Status = "TicketOwned";
        op.NextCheckAt = now.AddSeconds(30);
        op.CheckedAt = now;
        await db.SaveChangesAsync(token);

        if (config?.Enabled != true && ticket.Status == TicketStatus.Active && !ticket.ExpirySuspended && ticket.ExpiresAtUtc <= now)
        {
            using var newTicketGate = created ? await locks.TicketAsync(ticket.Id, token) : null;
            await using var recovery = await contexts.CreateDbContextAsync(token);
            var current = await recovery.Tickets
                .SingleAsync(value => value.Id == ticket.Id, token);

            await bans.ReconcileAsync(recovery, current, true, token, from);
        }
    }
}
