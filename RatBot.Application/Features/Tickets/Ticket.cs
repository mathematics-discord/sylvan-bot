namespace RatBot.Features.Tickets;

public sealed class Ticket
{
    public long Id { get; set; }
    public ulong GuildId { get; set; }
    public long Number { get; set; }
    public TicketAction Action { get; set; }
    public ulong? SubjectUserId { get; set; }
    public ulong? ModeratorUserId { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public TicketStatus? Status { get; set; }
    public long? DurationSeconds { get; set; }
    public ulong? RoleId { get; set; }
    public bool Hidden { get; set; }
    public ulong? ChannelId { get; set; }
    public ulong? MessageId { get; set; }
    public ulong? PreviousChannelId { get; set; }
    public ulong? PreviousMessageId { get; set; }
    public bool PublicationPending { get; set; }
    public ulong? LastModifiedByUserId { get; set; }
    public DateTimeOffset? LastModifiedAtUtc { get; set; }
    public int Version { get; set; }
}