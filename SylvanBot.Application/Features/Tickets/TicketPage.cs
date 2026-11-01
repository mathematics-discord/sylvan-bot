using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketPage(ImmutableArray<Ticket> Items, int Page, bool HasMore);