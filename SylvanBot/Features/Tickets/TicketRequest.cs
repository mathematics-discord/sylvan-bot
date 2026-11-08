namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public union TicketRequest(CreateNote, ObserveModeration, EditDetails, ReconcileTicket);

public sealed record CreateNote(TicketActor Actor, ulong SubjectId, string Comment);

public sealed record ObserveModeration(TicketObservation Observation, bool ReviewExempt, IReadOnlyDictionary<Guid, ulong> OperationAudits);

public sealed record EditDetails(Ticket Ticket, TicketActor Actor, TicketDetails Details);

public sealed record ReconcileTicket(Ticket Ticket, bool AuditCurrent);

public sealed record TicketResult(ImmutableArray<long> TicketIds, string Outcome);