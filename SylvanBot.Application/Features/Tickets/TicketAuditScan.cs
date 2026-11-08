using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketAuditScan(DotNext.Optional<ulong> LastAuditId, ImmutableArray<TicketObservation> Observations);