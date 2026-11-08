using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SylvanBot.Application.Features.SylvanMail;

internal static class SylvanMailSerialisation
{
    private const long LockId = 739416284;

    public static async Task EnterAsync(BotDbContext db, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({LockId})", cancellationToken: ct);
    }

    public static Task<int> ExitAsync(BotDbContext db) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({LockId})", cancellationToken: CancellationToken.None);

    public static async Task EnterConversationAsync(BotDbContext db, ulong userId, ILogger logger, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await db.Database.OpenConnectionAsync(ct);

        // Configuration changes remain exclusive, but different correspondents may run together.
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock_shared({LockId})", cancellationToken: ct);
        }
        finally
        {
            logger.LogDebug(
                "SylvanMail configuration lock wait for {UserId}: {ElapsedMs} ms",
                userId,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        started = Stopwatch.GetTimestamp();

        try
        {
            // PostgreSQL's two-integer lock space is separate from the configuration lock.
            var high = unchecked((int)(userId >> 32));
            var low = unchecked((int)userId);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({high}, {low})", cancellationToken: ct);
        }
        catch
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock_shared({LockId})", cancellationToken: CancellationToken.None);
            throw;
        }
        finally
        {
            logger.LogDebug(
                "SylvanMail conversation lock wait for {UserId}: {ElapsedMs} ms",
                userId,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public static async Task ExitConversationAsync(BotDbContext db, ulong userId)
    {
        var high = unchecked((int)(userId >> 32));
        var low = unchecked((int)userId);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({high}, {low})", cancellationToken: CancellationToken.None);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock_shared({LockId})", cancellationToken: CancellationToken.None);
        }
    }
}