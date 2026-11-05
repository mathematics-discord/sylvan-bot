namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class KickTicketStrategy : ITicketStrategy
{
    public TicketAction Action => TicketAction.Kick;

    public Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken token)
    {
        if (request is ObserveModeration observed)
            return BanTicketStrategy.RecordObservedAsync(db, observed, token);

        throw new ArgumentException("Unsupported kick request.", nameof(request));
    }
}
