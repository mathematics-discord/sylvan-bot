namespace RatBot.Features.Tickets;

public sealed class TicketNote
{
    public long TicketNumber { get; set; }
    public ulong GuildId { get; set; }
    public ulong SubjectUserId { get; set; }
    public ulong AuthorUserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public ulong? PublishedChannelId { get; set; }
    public ulong? PublishedMessageId { get; set; }
    public long? LegacyTicketNumber { get; set; }
}
