namespace SylvanBot.Features.Tickets;

public sealed record TicketPage(ImmutableArray<Ticket> Items, int Page, bool HasMore);