namespace RatBot.Features.TicketCollection;

public sealed class CapturedAuditEntry
{
    public required ulong GuildId { get; set; }
    public required ulong AuditId { get; set; }
    public required DateTimeOffset OccurredAtUtc { get; set; }
    public required DateTimeOffset CapturedAtUtc { get; set; }
    public ulong? ActorId { get; set; }
    public ulong? TargetId { get; set; }
    public string? Reason { get; set; }
    public required CapturedAuditAction Action { get; set; }
    public ulong[] AddedRoleIds { get; set; } = Array.Empty<ulong>();
    public ulong[] RemovedRoleIds { get; set; } = Array.Empty<ulong>();
    public bool? VoiceMuteBefore { get; set; }
    public bool? VoiceMuteAfter { get; set; }
    public bool? VoiceDeafenBefore { get; set; }
    public bool? VoiceDeafenAfter { get; set; }
    public bool HasTimeoutChange { get; set; }
    public DateTimeOffset? TimeoutBeforeUtc { get; set; }
    public DateTimeOffset? TimeoutAfterUtc { get; set; }
}