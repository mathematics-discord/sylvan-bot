using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketObservation
{
    public required ulong GuildId { get; init; }
    public required ulong AuditId { get; init; }
    public required TicketAction Action { get; init; }
    public required ulong SubjectUserId { get; init; }
    public required ulong ModeratorUserId { get; init; }
    public required bool ModeratorIsBot { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public ulong? RoleId { get; init; }
    public long? RestoredTicketNumber { get; init; }

    public bool Removed { get; init; }
    public bool RequiresApproval { get; init; }
    public Optional<DateTimeOffset> Timeout { get; init; } = Optional<DateTimeOffset>.None;
    public Optional<Guid> OperationId { get; init; } = Optional<Guid>.None;

    public bool CreatesTicket => !Removed
        && (Action != TicketAction.Timeout
            || (Timeout.TryGet(out var current) && current > CreatedAtUtc));

    public bool ContinuesTimeout(Ticket ticket) => Action == TicketAction.Timeout
        && ticket.Action == TicketAction.Timeout
        && ticket.GuildId == GuildId
        && ticket.SubjectUserId == SubjectUserId
        && ticket.Status == TicketStatus.Active
        && ticket.ExpiresAtUtc > CreatedAtUtc;
}


public sealed record TicketAuditScan(Optional<ulong> LastAuditId, ImmutableArray<TicketObservation> Observations);
