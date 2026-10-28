namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public sealed class TicketModerationEditModal : IModal
{
    public string Title { get; set; } = "Edit ticket";

    [InputLabel("Comment")]
    [ModalTextInput("comment", TextInputStyle.Paragraph, minLength: 1, maxLength: Ticket.CommentLimit)]
    public string Comment { get; set; } = string.Empty;

    [InputLabel("Duration", "From the original action time; leave blank to retain the current duration.")]
    [ModalTextInput("duration", maxLength: 100)]
    [RequiredInput(false)]
    public string Duration { get; set; } = string.Empty;
}