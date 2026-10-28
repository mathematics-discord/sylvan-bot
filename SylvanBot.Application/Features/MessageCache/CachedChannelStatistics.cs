namespace SylvanBot.Application.Features.MessageCache;

public sealed record CachedChannelStatistics
{
    public required ulong GuildId { get; init; }
    public required ulong ChannelId { get; init; }
    public required int Count { get; init; }
    public required DateTimeOffset OldestCreatedAtUtc { get; init; }
    public DateTimeOffset OldestExpiresAtUtc => OldestCreatedAtUtc.AddHours(24);
}