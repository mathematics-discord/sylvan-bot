namespace RatBot.Features.TicketCollection;

public sealed class AuditCollectionPosition
{
    public required ulong GuildId { get; set; }
    public required DateTimeOffset CaptureStartedAtUtc { get; set; }
    public ulong? LastScannedAuditId { get; set; }
}