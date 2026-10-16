namespace RatBot.Features.TicketCollection;

public sealed class TicketAuditObservation
{
    public required ulong GuildId { get; set; }
    public required ulong AuditId { get; set; }
    public required DateTimeOffset OccurredAtUtc { get; set; }
    public required DateTimeOffset CapturedAtUtc { get; set; }
    public ulong? ActorId { get; set; }
    public ulong? TargetId { get; set; }
    public string? Reason { get; set; }
    public required TicketAuditAction Action { get; set; }
}