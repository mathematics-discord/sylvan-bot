namespace SylvanBot.Application.Features.Tickets;

using Common;

public interface IDiscordTicket
{
    Task CheckChannelAsync(TicketActor actor, ulong channelId, bool publishing, CancellationToken cancellationToken);
    Task<Option<TicketMessage>> FindMessageAsync(TicketActor actor, Ticket ticket, CancellationToken cancellationToken);
    Task<TicketMessage> PublishAsync(Ticket ticket, ulong channelId, CancellationToken cancellationToken);

    // False means this link has no editable message owned by the current bot.
    Task<bool> UpdateAsync(Ticket ticket, TicketMessage publication, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(ulong guildId, TicketMessage publication, CancellationToken cancellationToken);
}