namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketConfiguration
{
    public IList<long> ReviewExemptRoleIds { get; set; } = [];

    public ulong GuildId { get; init; }

    public bool Enabled { get; set; }

    public ulong? DestinationChannelId { get; set; }

    public DateTimeOffset? CaptureFromUtc { get; set; }

    public ulong? LastAuditId { get; set; }

    public DateTimeOffset? LastAuditScanAtUtc { get; set; }

    public int ReminderIntervalSeconds { get; set; } = 86400;

    public void UpdateReviewExemptRoles(ulong? add, ulong? remove)
    {
        if (add is not null && !ReviewExemptRoleIds.Contains(checked((long)add.Value)))
            ReviewExemptRoleIds.Add(checked((long)add.Value));

        if (remove is not null)
            ReviewExemptRoleIds.Remove(checked((long)remove.Value));
    }
}
