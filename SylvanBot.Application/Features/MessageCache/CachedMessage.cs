namespace SylvanBot.Application.Features.MessageCache;

public sealed class CachedMessage
{
    public ulong BotId { get; init; }

    public ulong GuildId { get; init; }

    public ulong MessageId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public string SnapshotJson { get; init; } = string.Empty;
}
