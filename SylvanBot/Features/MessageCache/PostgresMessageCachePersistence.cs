using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.MessageCache;

public sealed class PostgresMessageCachePersistence(IDbContextFactory<BotDbContext> dbContextFactory, ILogger logger) : IMessageCachePersistence
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new JsonSerializerOptions { RespectNullableAnnotations = true };

    public async Task<ImmutableArray<CachedMessageSnapshot>> LoadAsync(
        ulong botId,
        ImmutableArray<ulong> guildIds,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var snapshots = ImmutableArray.CreateBuilder<CachedMessageSnapshot>();

        foreach (var guildId in guildIds)
        {
            ulong lastId = 0;

            while (true)
            {
                var pageAfterId = lastId;

                List<CachedMessage> rows = await db
                   .CachedMessages.AsNoTracking()
                   .Where(message =>
                              message.BotId == botId && message.GuildId == guildId && message.ExpiresAtUtc > now && message.MessageId > pageAfterId
                    )
                   .OrderBy(message => message.MessageId)
                   .Take(1000)
                   .ToListAsync(ct);

                foreach (var row in rows)
                    snapshots.Add(ReadSnapshot(row));

                if (rows.Count < 1000)
                    break;

                lastId = rows[^1].MessageId;
            }
        }

        return snapshots.ToImmutable();
    }

    public async Task SaveAsync(ulong botId, CachedMessageSnapshot snapshot, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var json = JsonSerializer.Serialize(snapshot);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO cached_messages (bot_id, guild_id, message_id, created_at_utc, expires_at_utc, snapshot)
             VALUES ({
                 (long)botId
             }, {
                 (long)snapshot.GuildId
             }, {
                 (long)
                 snapshot.MessageId
             }, {
                 snapshot.CreatedAtUtc
             }, {
                 snapshot.ExpiresAtUtc
             }, CAST({
                 json
             } AS jsonb))
             ON CONFLICT (bot_id, guild_id, message_id) DO UPDATE SET snapshot = EXCLUDED.snapshot
             """,
            cancellationToken: ct
        );
    }

    public async Task DeleteAsync(ulong botId, ulong guildId, ulong messageId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        await db
           .CachedMessages.Where(message => message.BotId == botId && message.GuildId == guildId && message.MessageId == messageId)
           .ExecuteDeleteAsync(ct);
    }

    public async Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        await db.CachedMessages.Where(message => message.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
    }

    private CachedMessageSnapshot ReadSnapshot(CachedMessage row)
    {
        try
        {
            var snapshot =
                JsonSerializer.Deserialize<CachedMessageSnapshot>(json: row.SnapshotJson, options: SnapshotJsonOptions)
                ?? throw new InvalidDataException("Stored message snapshot is null.");

            var consistent =
                snapshot.GuildId == row.GuildId
                && snapshot.MessageId == row.MessageId
                && snapshot.ChannelId != 0
                && snapshot.AuthorId != 0
                && snapshot.CreatedAtUtc == row.CreatedAtUtc
                && snapshot.ExpiresAtUtc == row.ExpiresAtUtc;

            if (!consistent || snapshot.Attachments.IsDefault)
                throw new InvalidDataException("Stored message snapshot is incomplete or inconsistent.");

            // JSON nullability enforcement does not cover collection elements.
            IEnumerable<MessageAttachment?> attachments = snapshot.Attachments;

            foreach (var attachment in attachments)
                if (attachment is null || string.IsNullOrWhiteSpace(attachment.Url) || attachment.Size < 0)
                    throw new InvalidDataException("Stored attachment metadata is invalid.");

            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            logger.Warning(
                "Invalid cached snapshot for bot {BotId}, guild {GuildId}, message {MessageId}: {ExceptionType}",
                row.BotId,
                row.GuildId,
                row.MessageId,
                exception.GetType().Name
            );

            throw;
        }
    }
}