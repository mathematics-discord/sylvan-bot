namespace SylvanBot.Application.Features.Tickets;

using Common;
using Microsoft.EntityFrameworkCore;

public sealed partial class TicketWorkflow
{
    public async Task<(Ticket? Ticket, int Ready, int Waiting)> ReviewQueueAsync(
        TicketActor actor,
        long after,
        CancellationToken cancellationToken)
    {
        actor = await discord.GetActorAsync(actor.GuildId, actor.UserId, cancellationToken);

        if (actor is { IsAdministrator: false, CanViewAuditLog: false })
            throw new BotCommandException("Review requires Administrator or View Audit Log.");

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var configuration = await dbContext.TicketConfigurations
           .AsNoTracking()
           .SingleOrDefaultAsync(value => value.GuildId == actor.GuildId, cancellationToken);

        if (configuration is not { Enabled: true, DestinationChannelId: not null })
            throw new BotCommandException("Tickets are not enabled in this server.");

        IQueryable<Ticket> pending = dbContext.Tickets
           .AsNoTracking()
           .Where(ticket => ticket.GuildId == actor.GuildId
                      && !ticket.Hidden
                      && !ticket.ReviewExempt
                      && ticket.ApprovedAtUtc == null
                      && (ticket.Action == TicketAction.Ban || ticket.Action == TicketAction.Timeout || ticket.Action == TicketAction.VoiceMute));

        List<ulong> channels = await pending
           .Select(ticket => ticket.ChannelId ?? configuration.DestinationChannelId.Value)
           .Distinct()
           .ToListAsync(cancellationToken);

        List<ulong> visible = new List<ulong>();

        foreach (var channel in channels)
        {
            try
            {
                await discord.CheckChannelAsync(actor, channel, publishing: false, cancellationToken);
                visible.Add(channel);
            }
            catch (BotCommandException)
            {
                // Inaccessible channels contribute neither tickets nor counts.
            }
        }

        pending = pending
           .Where(ticket => visible.Contains(ticket.ChannelId ?? configuration.DestinationChannelId.Value));

        IQueryable<Ticket> ready = pending
           .Where(ticket => !ticket.HidePending
                      && ticket.PendingOperationId == null
                      && ticket.Comment != null
                      && ticket.Comment.Trim() != string.Empty
                      && ticket.DurationKnown
                      && (ticket.Action != TicketAction.Timeout || ticket.DurationSeconds > 0)
                      && ticket.Completion != TicketCompletion.Pending);

        var count = await ready.CountAsync(cancellationToken);
        var total = await pending.CountAsync(cancellationToken);

        var next = await ready
           .Where(ticket => ticket.Number > after)
           .OrderBy(ticket => ticket.Number)
           .FirstOrDefaultAsync(cancellationToken);

        return (next, count, total - count);
    }
}