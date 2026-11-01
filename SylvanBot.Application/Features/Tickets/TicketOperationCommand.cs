namespace SylvanBot.Application.Features.Tickets;

public union TicketOperationCommand(TicketOperationCommand.ChangeTimeout, TicketOperationCommand.ExpireBan)
{
    public sealed record ChangeTimeout(DateTimeOffset ExpiryUtc);

    public sealed record ExpireBan;
}