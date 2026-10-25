namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketConfiguration
{
    public ulong GuildId { get; init; }
    public bool Enabled { get; set; }
    public ulong? DestinationChannelId { get; set; }
    public DateTimeOffset? CaptureFromUtc { get; set; }
    public ulong? LastAuditId { get; set; }
    public DateTimeOffset? LastAuditScanAtUtc { get; set; }
    public int ReminderIntervalSeconds { get; set; } = 86400;
}
