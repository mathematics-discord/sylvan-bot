namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketConfiguration
{
    public IList<long> TrackedRoleIds { get; set; } = [];

    public ulong GuildId { get; init; }

    public bool Enabled { get; set; }

    public ulong? DestinationChannelId { get; set; }

    public ulong? ReviewSummaryChannelId { get; set; }

    public ulong? ReviewSummaryMessageId { get; set; }

    public DateTimeOffset? CaptureFromUtc { get; set; }

    public ulong? LastAuditId { get; set; }

    public DateTimeOffset? LastAuditScanAtUtc { get; set; }

    public int ReminderIntervalSeconds { get; set; } = 86400;

    public ulong PublicationChannel(Ticket ticket) => ticket.Publications
        .OrderByDescending(publication => publication.MessageId)
        .Select(publication => (ulong?)publication.ChannelId)
        .FirstOrDefault()
        ?? DestinationChannelId
        ?? throw new InvalidOperationException("Tickets have no configured destination.");
}
