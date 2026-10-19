namespace RatBot.Features.Tickets;

public sealed record TicketPage(ImmutableArray<Ticket> Items, int Page, bool HasMore);