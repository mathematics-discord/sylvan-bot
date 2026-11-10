namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketObservation(
    ulong GuildId,
    ulong AuditId,
    TicketAction Action,
    ulong SubjectUserId,
    ulong ModeratorUserId,
    bool ModeratorIsBot,
    string Reason,
    DateTimeOffset CreatedAtUtc)
{
    public ulong? RoleId { get; init; }
    public long? RestoredTicketNumber { get; init; }

    public bool Removed { get; init; }
    public TicketTimeout Timeout { get; init; } = new TicketTimeout.None();
    public DotNext.Optional<Guid> OperationId { get; init; }

    public bool CreatesTicket => !Removed
        && (Action != TicketAction.Timeout
            || (Timeout.Value is TicketTimeout.Until current && current.ExpiresAtUtc > CreatedAtUtc));

    public bool ContinuesTimeout(Ticket ticket) => Action == TicketAction.Timeout
        && ticket.Action == TicketAction.Timeout && ticket.GuildId == GuildId
        && ticket.SubjectUserId == SubjectUserId && ticket.Status == TicketStatus.Active
        && ticket.ExpiresAtUtc > CreatedAtUtc;
}
