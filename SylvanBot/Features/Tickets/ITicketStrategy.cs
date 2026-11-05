using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.Tickets;

public interface ITicketStrategy
{
    TicketAction Action { get; }
    Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken cancellationToken);
}