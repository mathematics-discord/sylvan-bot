namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public union TicketRequest(CreateNote, ObserveModeration, EditDetails, ReconcileTicket);
public sealed record CreateNote(TicketActor Actor, ulong SubjectId, string Comment);
public sealed record ObserveModeration(TicketObservation Observation, bool ReviewExempt, IReadOnlyDictionary<Guid, ulong> OperationAudits);
public sealed record EditDetails(Ticket Ticket, TicketActor Actor, TicketDetails Details);
public sealed record ReconcileTicket(Ticket Ticket, bool AuditCurrent);
public sealed record TicketResult(ImmutableArray<long> TicketIds, string Outcome);

public interface ITicketStrategy
{
    TicketAction Action { get; }
    Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken cancellationToken);
}

public sealed class NoteTicketStrategy : ITicketStrategy
{
    public TicketAction Action => TicketAction.Note;

    public async Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken cancellationToken)
    {
        if (request is not CreateNote note)
            throw new ArgumentException("Expected note creation.", nameof(request));

        Ticket.ValidateComment(note.Comment);
        var number = await db.Database
            .SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"")
            .SingleAsync(cancellationToken);

        var ticket = new Ticket
        {
            GuildId = note.Actor.GuildId,
            Number = number,
            SubjectUserId = note.SubjectId,
            ModeratorUserId = note.Actor.UserId,
            Action = Action,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        ticket.Initialise(note.Comment, null);
        db.Tickets.Add(ticket);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SylvanBot.Application.Common.BotCommandException(
                $"Saving ticket #{number} could not be confirmed. No publication was attempted. Read the ticket before creating another note.", exception);
        }
        return new TicketResult([ticket.Id], $"Saved ticket #{number}.");
    }
}
