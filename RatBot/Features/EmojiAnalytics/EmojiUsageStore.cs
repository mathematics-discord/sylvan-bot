using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Features.EmojiAnalytics;

public sealed class EmojiUsageStore(IDbContextFactory<BotDbContext> dbContextFactory)
{
    public async Task RecordBatchAsync(
        ulong guildId,
        IReadOnlyCollection<EmojiUsageIncrement> increments,
        IReadOnlyCollection<ulong> trackedEmojiIds,
        CancellationToken ct = default
    )
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);
        HashSet<ulong> tracked = trackedEmojiIds.ToHashSet();

        EmojiUsageCount[] rows = await db.EmojiUsageCounts.Where(row => row.GuildId == guildId).ToArrayAsync(ct);

        db.EmojiUsageCounts.RemoveRange(rows.Where(row => !tracked.Contains(row.EmojiId)));

        Dictionary<ulong, EmojiUsageCount> rowsById = rows.ToDictionary(row => row.EmojiId);

        foreach (EmojiUsageIncrement increment in increments)
        {
            if (increment.GuildId != guildId || !tracked.Contains(increment.EmojiId))
                continue;

            if (!rowsById.TryGetValue(increment.EmojiId, out EmojiUsageCount? row))
            {
                row = new EmojiUsageCount
                {
                    GuildId = guildId,
                    EmojiId = increment.EmojiId,
                    MessageUsageCount = 0,
                    ReactionUsageCount = 0
                };

                db.EmojiUsageCounts.Add(row);
                rowsById.Add(row.EmojiId, row);
            }

            row.MessageUsageCount += increment.MessageCount;
            row.ReactionUsageCount += increment.ReactionCount;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<ErrorOr<EmojiUsagePage>> GetUsagePageAsync(
        ulong guildId,
        IReadOnlyCollection<ulong> trackedEmojiIds,
        int page,
        int pageSize = 25,
        CancellationToken ct = default
    )
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<EmojiUsageCount> query = db
            .EmojiUsageCounts.AsNoTracking()
            .Where(row => row.GuildId == guildId && trackedEmojiIds.Contains(row.EmojiId));

        int totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return Error.NotFound(description: "No emoji usage has been recorded yet.");

        int clampedPageSize = Math.Clamp(pageSize, 1, 100);
        int totalPages = (int)Math.Ceiling((double)totalCount / clampedPageSize);
        int clampedPage = Math.Clamp(page, 1, totalPages);

        ImmutableArray<EmojiUsageEntry>.Builder
            builder = ImmutableArray.CreateBuilder<EmojiUsageEntry>(clampedPageSize);

        IQueryable<EmojiUsageEntry> items = query
            .OrderByDescending(row => row.ReactionUsageCount + row.MessageUsageCount)
            .ThenBy(row => row.EmojiId)
            .Skip((clampedPage - 1) * clampedPageSize)
            .Take(clampedPageSize)
            .Select(row => new EmojiUsageEntry(row.EmojiId, row.MessageUsageCount, row.ReactionUsageCount));

        await foreach (EmojiUsageEntry item in items.AsAsyncEnumerable().WithCancellation(ct))
            builder.Add(item);

        return new EmojiUsagePage(builder.ToImmutable(), clampedPage, totalPages);
    }
}