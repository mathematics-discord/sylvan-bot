namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public sealed record ObserveModeration(TicketObservation Observation, bool CanReview, IReadOnlyDictionary<Guid, ulong> OperationAudits, bool CaptureEnabled);

public sealed record EditDetails(Ticket Ticket, TicketActor Actor, TicketDetails Details);

public sealed record TicketResult(ImmutableArray<long> TicketIds, string Outcome);