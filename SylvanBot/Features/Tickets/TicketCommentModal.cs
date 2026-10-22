namespace SylvanBot.Features.Tickets;

public sealed class TicketCommentModal : IModal
{
    public string Title { get; set; } = "Edit ticket";

    [InputLabel("Comment")]
    [ModalTextInput("comment", TextInputStyle.Paragraph, minLength: 1, maxLength: TicketWorkflow.CommentLimit)]
    public string Comment { get; set; } = string.Empty;
}