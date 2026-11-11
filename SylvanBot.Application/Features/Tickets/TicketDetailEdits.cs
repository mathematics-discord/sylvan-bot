#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).
// And, more importantly, RIDER is an asshole who doesn't understand a union can be null.
namespace SylvanBot.Application.Features.Tickets;

public union TicketCommentEdit(TicketCommentEdit.Keep, TicketCommentEdit.Replace, TicketCommentEdit.Append)
{
    public sealed record Keep;

    public sealed record Replace(string Text);

    public sealed record Append(string Text);

    public string Apply(string current) => this.Value switch
    {
        Keep => current,
        Replace replacement => replacement.Text,
        Append addition => current.Length == 0 ? addition.Text : current + "\n" + addition.Text,
    };
}

public union TicketDurationEdit(TicketDurationEdit.Keep, TicketDurationEdit.Set, TicketDurationEdit.Permanent)
{
    public sealed record Keep;

    public sealed record Set(long Seconds);

    public sealed record Permanent;
}