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
        if (ticket.ApprovedAtUtc is not null)
            return false;

        var actor = await boundary.GetActorAsync(guildId, userId, token);
        var previous = ticket.Version;

        if (!ticket.TryApproveReaction(actor, channelId, messageId))
            return false;

        await PersistApprovalAsync(db, ticket, previous, token);
        logger.Information("Ticket {TicketNumber} approved by reaction from {UserId} on message {MessageId}", ticket.Number, userId, messageId);
        return true;
    }

    private async Task<TicketResult> PersistApprovalAsync(BotDbContext db, Ticket ticket, int previousVersion, CancellationToken token)
    {
        using var timing = new OperationTiming(logger.ForContext("TicketId", ticket.Id), "ticket.approval_save", "database");
        await SaveAsync(db, token);

        if (ticket.Version == previousVersion)
            return new TicketResult([], $"Approved ticket #{ticket.Number}.");

        // Approval is durable before cleanup. Neither duplicate reactions nor recovery
        // retries this deletion, even if Discord cannot confirm the one attempt.
        timing.Next("clear_reactions");
        await ClearApprovalReactionsAsync(ticket, token);
        timing.Next("publication");

        return await PublishAsync(ticket.GuildId, new TicketResult([ticket.Id], $"Approved ticket #{ticket.Number}."), token);
    }

    private async Task ClearApprovalReactionsAsync(Ticket ticket, CancellationToken token)
    {
        var publication = ticket.Publications
            .SingleOrDefault(reference => reference.Current && !reference.Removed);

        if (publication is null)
            return;

        try
        {
            var channel = await ((IDiscordClient)client.Rest).GetChannelAsync(publication.ChannelId, options: TicketBoundary.Options(token));

            if (channel is not ITextChannel textChannel || textChannel.GuildId != ticket.GuildId)
                return;

            if (await textChannel.GetMessageAsync(publication.MessageId, options: TicketBoundary.Options(token)) is IUserMessage message
                && message.Author.Id == client.CurrentUser.Id)
                await message.RemoveAllReactionsAsync(TicketBoundary.Options(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Ticket {TicketId} was approved, but its one reaction cleanup attempt failed on message {MessageId}",
                ticket.Id, publication.MessageId);
        }
    }
}
