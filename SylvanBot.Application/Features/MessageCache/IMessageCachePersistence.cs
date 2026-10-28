using System.Collections.Immutable;

namespace SylvanBot.Application.Features.MessageCache;

/// <summary>Durable snapshot storage, scoped to a stable bot identity and its configured guilds.</summary>
public interface IMessageCachePersistence
{
    Task<ImmutableArray<CachedMessageSnapshot>> LoadAsync(ulong botId, ImmutableArray<ulong> guildIds, DateTimeOffset now, CancellationToken ct);

    Task SaveAsync(ulong botId, CachedMessageSnapshot snapshot, CancellationToken ct);

    Task DeleteAsync(ulong botId, ulong guildId, ulong messageId, CancellationToken ct);

    Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct);
}