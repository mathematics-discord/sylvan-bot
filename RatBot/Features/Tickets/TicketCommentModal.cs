namespace RatBot.Features.Tickets;

public sealed class TicketCommentModal : IModal
{
    public string Title { get; set; } = "Edit ticket";

    [InputLabel("Comment")]
    [ModalTextInput("comment", TextInputStyle.Paragraph, minLength: 1, maxLength: 4000)]
    public string Comment { get; set; } = string.Empty;
}