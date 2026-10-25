namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public sealed class TicketEditModal : IModal
{
    public string Title { get; set; } = "Edit ticket";

    [InputLabel("Comment")]
    [ModalTextInput("comment", TextInputStyle.Paragraph, minLength: 1, maxLength: Ticket.CommentLimit)]
    public string Comment { get; set; } = string.Empty;

    [InputLabel("Hidden ticket", "Hidden tickets can only be viewed by senior moderators and administrators.")]
    [ModalCheckbox("hidden")]
    public bool Hidden { get; set; }
}