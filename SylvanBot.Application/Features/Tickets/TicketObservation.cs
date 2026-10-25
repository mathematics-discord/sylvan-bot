namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketObservation(
    ulong GuildId,
    ulong AuditId,
    TicketAction Action,
    ulong? SubjectUserId,
    ulong? ModeratorUserId,
    bool ModeratorIsBot,
    string? Reason,
    DateTimeOffset CreatedAtUtc,
    bool Removed = false,
    DateTimeOffset? PreviousExpiryUtc = null,
    DateTimeOffset? ExpiryUtc = null,
    Guid? OperationId = null,
    bool PermanentBotAction = false)
{
    public bool CreatesTicket => !Removed
                                 && (Action == TicketAction.Ban
                                     || PreviousExpiryUtc is null
                                     || PreviousExpiryUtc <= CreatedAtUtc
                                     || ExpiryUtc > PreviousExpiryUtc);
}
