namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketConfiguration
{
    public IList<long> TrackedRoleIds { get; set; } = [];

    public IList<long> ReviewExemptRoleIds { get; set; } = [];

    public ulong GuildId { get; init; }

    public bool OrderedPublicationPending { get; set; }

    public bool Enabled { get; set; }

    public ulong? DestinationChannelId { get; set; }

    public ulong? LegacyChannelId { get; set; }

    public ulong? ReviewSummaryChannelId { get; set; }

    public ulong? ReviewSummaryMessageId { get; set; }

    public DateTimeOffset? CaptureFromUtc { get; set; }

    public ulong? LastAuditId { get; set; }

    public DateTimeOffset? LastAuditScanAtUtc { get; set; }

    public int ReminderIntervalSeconds { get; set; } = 86400;

    public bool AwaitsOrderedPublication(Ticket ticket) =>
        OrderedPublicationPending && (LegacyChannelId is null || ticket.ImportedFromLegacy);

    public ulong PublicationChannel(Ticket ticket) =>
        ticket.PinnedPublicationChannelId
        ?? (ticket.ImportedFromLegacy ? LegacyChannelId : null)
        ?? DestinationChannelId
        ?? throw new InvalidOperationException("Tickets have no configured destination.");

    public void UpdateReviewExemptRoles(ulong? add, ulong? remove)
    {
        if (add is not null && !ReviewExemptRoleIds.Contains(checked((long)add.Value)))
            ReviewExemptRoleIds.Add(checked((long)add.Value));

        if (remove is not null)
            ReviewExemptRoleIds.Remove(checked((long)remove.Value));
    }
}
