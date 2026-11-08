using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.Tickets;

public sealed class NoteTicketStrategy : ITicketStrategy
{
    public TicketAction Action => TicketAction.Note;

    public async Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken cancellationToken)
    {
        if (request.Value is not CreateNote note)
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
                $"Saving ticket #{number} could not be confirmed. No publication was attempted. Read the ticket before creating another note.",
                exception);
        }

        return new TicketResult([ticket.Id], $"Saved ticket #{number}.");
    }
}