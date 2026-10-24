namespace SylvanBot.Application.Features.Tickets;

using System.Collections.Immutable;

public sealed record TicketPage(ImmutableArray<Ticket> Items, int Page, bool HasMore);