using RatBot.Domain.Common;

namespace RatBot.Domain.Features.Logging;

public sealed class LoggingConfiguration
{
    public static readonly TimeSpan DefaultEvidenceRetentionPeriod = TimeSpan.FromMinutes(15);

    public LoggingConfiguration(
        ulong guildId,
        bool enabled,
        ulong? deleteLogChannelId,
        ulong? editLogChannelId,
        TimeSpan evidenceRetentionPeriod)
    {
        if (guildId == 0)
            throw new ArgumentOutOfRangeException(nameof(guildId), "Guild id is required.");

        if (evidenceRetentionPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(evidenceRetentionPeriod),
                "Evidence retention period must be positive.");

        if (enabled && deleteLogChannelId is null && editLogChannelId is null)
            throw new ArgumentException("Enabled logging requires at least one log channel.", nameof(enabled));

        GuildId = guildId;
        Enabled = enabled;
        DeleteLogChannelId = deleteLogChannelId;
        EditLogChannelId = editLogChannelId;
        EvidenceRetentionPeriod = evidenceRetentionPeriod;
    }

    public ulong GuildId { get; }
    public bool Enabled { get; }
    public ulong? DeleteLogChannelId { get; }
    public ulong? EditLogChannelId { get; }
    public TimeSpan EvidenceRetentionPeriod { get; }

    public static LoggingConfiguration Disabled(ulong guildId) =>
        new LoggingConfiguration(
            guildId: guildId,
            enabled: false,
            deleteLogChannelId: null,
            editLogChannelId: null,
            evidenceRetentionPeriod: DefaultEvidenceRetentionPeriod);

    public bool AllowsLogging(bool channelIsExcluded) => Enabled && !channelIsExcluded;

    public Option<ulong> GetDestinationChannelId(LoggingEventKind eventKind)
    {
        ulong? channelId = eventKind is LoggingEventKind.Edit
            ? EditLogChannelId ?? DeleteLogChannelId
            : DeleteLogChannelId ?? EditLogChannelId;

        return channelId is { } value ? new Some<ulong>(value) : new None();
    }

    public LoggingConfiguration WithUpdate(
        bool? enabled,
        ulong? deleteLogChannelId,
        ulong? editLogChannelId,
        TimeSpan? evidenceRetentionPeriod)
    {
        return new LoggingConfiguration(
            GuildId,
            enabled ?? Enabled,
            deleteLogChannelId ?? DeleteLogChannelId,
            editLogChannelId ?? EditLogChannelId,
            evidenceRetentionPeriod ?? EvidenceRetentionPeriod
        );
    }
}