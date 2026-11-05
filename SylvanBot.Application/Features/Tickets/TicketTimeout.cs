namespace SylvanBot.Application.Features.Tickets;

public union TicketTimeout(TicketTimeout.None, TicketTimeout.Until)
{
    public sealed record None;

    public sealed record Until(DateTimeOffset ExpiresAtUtc);
}