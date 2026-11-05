namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class VoiceDeafenTicketStrategy(DiscordSocketClient client, TicketBoundary boundary, ILogger logger) : ITicketStrategy
{
    public TicketAction Action => TicketAction.VoiceDeafen;

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
            throw new ArgumentException("Unsupported voice request.", nameof(request));

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

        var config = await db.TicketConfigurations
            .SingleAsync(value => value.GuildId == ticket.GuildId, token);
        var scan = await boundary.ReadAuditAsync(ticket.GuildId, config.LastAuditScanAtUtc!.Value.AddMinutes(-10), token);
        var newer = scan.Observations
            .Any(value => value.Action == Action && value.SubjectUserId == ticket.SubjectUserId
                && value.AuditId > ticket.LastAuditId
                && (!value.OperationId.HasValue || ticket.Pending == null || value.OperationId.Value != ticket.Pending.Id));

        if (newer)
            return new TicketResult([], "A newer voice change awaits reconciliation.");

        var member = await client.Rest.GetGuildUserAsync(ticket.GuildId, ticket.SubjectUserId, TicketBoundary.Options(token));
        if (member is null || !member.IsDeafened)
        {
            ticket.End(TicketStatus.Expired);
            ticket.Pending = null;
        }
        else
        {
            if (ticket.Pending is null)
            {
                ticket.Pending = new TicketOperation { Kind = "RemoveVoiceDeafen" };
                ticket.Record(TicketHistoryAction.AuditObserved);
            }
            await db.SaveChangesAsync(token);
            var connected = client.GetGuild(ticket.GuildId)?.GetUser(ticket.SubjectUserId)?.VoiceChannel is not null;
            if (!connected)
                return new TicketResult([ticket.Id], "Expiry is waiting for the member to reconnect to voice.");

            if (ticket.Pending.Attempted && ticket.Pending.RequestedAtUtc > DateTimeOffset.UtcNow.AddSeconds(-30))
                return new TicketResult([], "Voice expiry is awaiting confirmation.");

            ticket.Pending.Attempted = true;
            ticket.Pending.RequestedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
            var options = TicketBoundary.Options(token);
            options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {ticket.Pending.Id:D}";

            try
            {
                await member.ModifyAsync(properties => properties.Deaf = false, options);
                ticket.End(TicketStatus.Expired);
                ticket.Pending = null;
            }
            catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden)
            {
                logger.Warning("Voice expiry blocked for ticket {TicketId}: {Code}", ticket.Id, exception.DiscordCode);
                return new TicketResult([ticket.Id], "Voice expiry remains pending. Check voice connection and permissions.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.Warning("Voice expiry uncertain for ticket {TicketId}: {Failure}", ticket.Id, exception.GetType().Name);
                return new TicketResult([ticket.Id], "Voice expiry could not be confirmed; recovery will reconcile it.");
            }
        }

        await db.SaveChangesAsync(token);
        return new TicketResult([ticket.Id], "Voice expiry reconciled.");
    }
}
