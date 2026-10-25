using System.ComponentModel.DataAnnotations;

namespace SylvanBot.Application.Features.Tickets;

public sealed class Ticket
{
    public const int CommentLimit = 3000;

    public long Id { get; init; }
    public ulong GuildId { get; init; }
    public long Number { get; init; }
    public TicketAction Action { get; init; }
    public ulong? SubjectUserId { get; init; }
    public ulong? ModeratorUserId { get; init; }

    [StringLength(CommentLimit)]
    public string? Comment
    {
        get;
        set
        {
            if (value?.Length > CommentLimit)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Ticket comments cannot exceed {CommentLimit:N0} characters.");

            field = value;
        }
    }

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