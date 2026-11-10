using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketAuditScan(Optional<ulong> LastAuditId, ImmutableArray<TicketObservation> Observations);