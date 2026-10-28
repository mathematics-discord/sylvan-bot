namespace SylvanBot.Features.Logging;

public sealed class LoggingConfiguration
{
    public ulong GuildId { get; set; }
    public bool Enabled { get; set; }
    public ulong? DeleteLogChannelId { get; set; }
    public ulong? EditLogChannelId { get; set; }

    // Inert compatibility value for evidence_retention_period until schema retirement.
    // Only new rows use this initial value; commands leave stored values unchanged.
    public TimeSpan LegacyEvidenceRetentionPeriod { get; set; } = TimeSpan.FromMinutes(15);
}
