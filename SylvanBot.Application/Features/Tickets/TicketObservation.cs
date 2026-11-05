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
    public TicketTimeout PreviousTimeout { get; init; } = new TicketTimeout.None();
    public TicketTimeout Timeout { get; init; } = new TicketTimeout.None();
    public DotNext.Optional<Guid> OperationId { get; init; }

    public bool CreatesTicket => !Removed
        && (Action != TicketAction.Timeout
            || PreviousTimeout.Value is TicketTimeout.None
            || (PreviousTimeout.Value is TicketTimeout.Until previous
                && (previous.ExpiresAtUtc <= CreatedAtUtc
                    || (Timeout.Value is TicketTimeout.Until current && current.ExpiresAtUtc > previous.ExpiresAtUtc))));
}