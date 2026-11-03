using static System.StringComparison;

namespace SylvanBot.Application.Features.Automod;

public sealed class AutomodBan
{
    public Guid Id { get; init; }

    public ulong GuildId { get; init; }

    public ulong UserId { get; init; }

    public ulong MessageId { get; init; }

    public long RuleId { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    // Nullable storage boundary; application decisions use explicit optionality.
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public Optional<DateTimeOffset> ExpiresAt
    {
        get => ExpiresAtUtc is { } expiry ? new Optional<DateTimeOffset>(expiry) : default;
        set => ExpiresAtUtc = value.HasValue ? value.Value : null;
    }

    public DateTimeOffset NextCheckAt { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    public ulong AuditId { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTimeOffset LastAttemptAt { get; set; }

    public string Reason => $"Automod ban rule {RuleId}; automod operation {Id:D}";

    public void Observe(ulong auditId, bool ownBan, bool ownRemoval)
    {
        if (auditId <= AuditId)
            return;

        if (AuditId == 0 && ownBan)
        {
            AuditId = auditId;
            Status = "Active";
        }
        else
        {
            Status = ownRemoval && string.Equals(Status, "Removing", Ordinal) ? "Ended" : "Superseded";
        }
    }

    public bool AuditCoverageLost(DateTimeOffset now) => now - CheckedAt >= TimeSpan.FromDays(45);

    public bool CanExpire(DateTimeOffset now) => AuditId != 0
                                                 && Status is "Active" or "Removing"
                                                 && ExpiresAt.HasValue
                                                 && ExpiresAt.Value <= now
                                                 && LastAttemptAt <= now.AddSeconds(-30);
}