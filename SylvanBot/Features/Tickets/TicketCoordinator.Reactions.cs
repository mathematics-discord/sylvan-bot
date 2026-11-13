namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed partial class TicketCoordinator
{
    internal async Task<bool> ApproveReactionAsync(ulong guildId, ulong channelId, ulong messageId, ulong userId, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var id = await db.Tickets
            .Where(ticket => ticket.GuildId == guildId && !ticket.Hidden
                && ticket.ApprovedAtUtc == null
                && ticket.Status != TicketStatus.Reverted
                && ticket.Publications.Any(reference => reference.ChannelId == channelId && reference.MessageId == messageId
                    && reference.Current && !reference.Removed))
            .Select(ticket => (long?)ticket.Id)
            .SingleOrDefaultAsync(token);

        if (id is null)
            return false;

        using var gate = await locks.TicketAsync(id.Value, token);
        await EnabledAsync(db, guildId, token);
        var ticket = await db.Tickets
            .SingleAsync(value => value.Id == id.Value, token);

        // Recheck after taking the same lock used by commands and publication. A second
        // reaction must neither replace the approver nor clear reactions again.
        if (ticket.IsApproved)
            return false;

        var actor = await boundary.GetActorAsync(guildId, userId, token);
        var previous = ticket.Version;

        if (!ticket.TryApproveReaction(actor, channelId, messageId))
            return false;

        await PersistApprovalAsync(db, ticket, previous, token);
        logger.Information("Ticket {TicketNumber} approved by reaction from {UserId} on message {MessageId}", ticket.Number, userId, messageId);
        return true;
    }

    private async Task<string> PersistApprovalAsync(BotDbContext db, Ticket ticket, int previousVersion, CancellationToken token)
    {
        using var timing = new OperationTiming(logger.ForContext("TicketId", ticket.Id), "ticket.approval_save", "database");
        var changed = ticket.Version != previousVersion;
        var publication = ticket.Publications
            .SingleOrDefault(reference => reference.Current && !reference.Removed);

        if (changed && publication is not null)
            publication.ApprovalReactionCleanupPending = true;

        await SaveAsync(db, token);
        return Queued($"Approved ticket #{ticket.Number}.", changed);
    }
}
