namespace SylvanBot.Features.Tickets;

public sealed record TicketChange(
    Ticket Ticket, TicketMessage? ConfirmedPublication = null, bool ReferenceSaved = true,
    bool PublicationUncertain = false, bool LinkedCopyRemains = false, bool NoChange = false);