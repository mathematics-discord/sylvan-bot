using System.ComponentModel.DataAnnotations;

namespace SylvanBot.Application.Features.Tickets;

public sealed class Ticket
{
    public long Id { get; init; }
    public ulong GuildId { get; init; }
    public long Number { get; init; }
    public TicketAction Action { get; init; }
    public ulong? SubjectUserId { get; init; }
    public ulong? ModeratorUserId { get; init; }
    [StringLength(1900)] public string? Comment { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public TicketStatus? Status { get; init; }
    public long? DurationSeconds { get; init; }
    public ulong? RoleId { get; init; }
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