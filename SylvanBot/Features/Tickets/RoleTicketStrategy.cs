namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RolePersistence;

public sealed class RoleTicketStrategy(DiscordSocketClient client, TicketBoundary boundary, RolePersistenceWorker persistence, ILogger logger) : ITicketStrategy
{
    public TicketAction Action => TicketAction.Role;

    public async Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken token)
    {
        if (request is ObserveModeration observed)
            return await BanTicketStrategy.RecordObservedAsync(db, observed, token);

        if (request is EditDetails edit)
        {
            edit.Ticket.SetDuration(edit.Details.DurationSeconds == long.MaxValue ? null : edit.Details.DurationSeconds, edit.Actor.UserId);
            await db.SaveChangesAsync(token);
            return new TicketResult([edit.Ticket.Id], "Duration saved. Expiry will follow audit reconciliation.");
        }

        if (request is not ReconcileTicket recovery)
            throw new ArgumentException("Unsupported role request.", nameof(request));

        var ticket = recovery.Ticket;
        if (ticket.SourceAuditId is null)
            ticket.ExpirySuspended = true;

        if (ticket.Status != TicketStatus.Active)
        {
            ticket.Pending = null;
            return new TicketResult([], string.Empty);
        }

        if (!recovery.AuditCurrent || ticket.ExpirySuspended || ticket.ExpiresAtUtc is null || ticket.ExpiresAtUtc > DateTimeOffset.UtcNow)
            return new TicketResult([], string.Empty);

        var gate = persistence.Gate(ticket.GuildId);
        await gate.WaitAsync(token);
        try
        {
            var config = await db.TicketConfigurations
                .SingleAsync(value => value.GuildId == ticket.GuildId, token);
            var scan = await boundary.ReadAuditAsync(ticket.GuildId, config.LastAuditScanAtUtc!.Value.AddMinutes(-10), token);
            var newer = scan.Observations
                .Any(value => value.Action == Action && value.SubjectUserId == ticket.SubjectUserId && value.RoleId == ticket.RoleId
                    && value.AuditId > ticket.LastAuditId
                    && (!value.OperationId.HasValue || ticket.Pending == null || value.OperationId.Value != ticket.Pending.Id));

            if (newer)
                return new TicketResult([], "A newer role change awaits reconciliation.");

            if (ticket.Pending is null)
            {
                ticket.Pending = new TicketOperation { Kind = "RemoveRole" };
                ticket.Record(TicketHistoryAction.AuditObserved);
            }
            await db.SaveChangesAsync(token);
            var member = await client.Rest.GetGuildUserAsync(ticket.GuildId, ticket.SubjectUserId, TicketBoundary.Options(token));
            if (member is not null && member.RoleIds.Contains(ticket.RoleId!.Value))
            {
                if (ticket.Pending.Attempted && ticket.Pending.RequestedAtUtc > DateTimeOffset.UtcNow.AddSeconds(-30))
                    return new TicketResult([], "Role removal awaits confirmation.");

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
                    return new TicketResult([ticket.Id], "Role removal remains pending. Recovery will check membership, permissions and hierarchy.");
                }
            }

            await db.PendingRoles
                .Where(value => value.GuildId == ticket.GuildId && value.UserId == ticket.SubjectUserId && value.RoleId == ticket.RoleId)
                .ExecuteDeleteAsync(token);
            ticket.End(TicketStatus.Expired);
            ticket.Pending = null;
            await db.SaveChangesAsync(token);
            return new TicketResult([ticket.Id], "Role expiry reconciled.");
        }
        finally
        {
            gate.Release();
        }
    }
}
