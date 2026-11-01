namespace SylvanBot.Application.Features.Tickets;

public enum TicketStatus
{
    NotApplicable = -1,
    Active = 0,
    Expired = 1,
    Reverted = 3,
    Applied = 4,
    Superseded = 5,
}