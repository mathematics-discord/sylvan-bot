namespace SylvanBot.Application.Features.Tickets;

using System.Collections.Immutable;

public sealed record TicketObservation(ulong GuildId, ulong AuditId, TicketAction Action, ulong SubjectUserId,
    ulong ModeratorUserId, bool ModeratorIsBot, string Reason, DateTimeOffset CreatedAtUtc)
{
    public bool Removed { get; init; }
    public TicketTimeout PreviousTimeout { get; init; } = new TicketTimeout.None();
    public TicketTimeout Timeout { get; init; } = new TicketTimeout.None();
    public DotNext.Optional<Guid> OperationId { get; init; }

    public bool CreatesTicket => !Removed
        && (Action != TicketAction.Timeout
            || PreviousTimeout is TicketTimeout.None
            || (PreviousTimeout is TicketTimeout.Until previous
                && (previous.ExpiresAtUtc <= CreatedAtUtc
                    || (Timeout is TicketTimeout.Until current && current.ExpiresAtUtc > previous.ExpiresAtUtc))));
}

public union TicketTimeout(TicketTimeout.None, TicketTimeout.Until)
{
    public sealed record None;
    public sealed record Until(DateTimeOffset ExpiresAtUtc);
}

public sealed record TicketAuditScan(DotNext.Optional<ulong> LastAuditId, ImmutableArray<TicketObservation> Observations);
