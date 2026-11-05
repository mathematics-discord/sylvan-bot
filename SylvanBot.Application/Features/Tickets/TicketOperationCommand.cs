namespace SylvanBot.Application.Features.Tickets;

public union TicketOperationCommand(TicketOperationCommand.ChangeTimeout, TicketOperationCommand.ExpireBan, TicketOperationCommand.ExpireVoiceMute, TicketOperationCommand.ExpireVoiceDeafen, TicketOperationCommand.ExpireRole)
{
    public sealed record ChangeTimeout(DateTimeOffset ExpiryUtc);

    public sealed record ExpireBan;
    public sealed record ExpireVoiceMute;
    public sealed record ExpireVoiceDeafen;
    public sealed record ExpireRole;
}