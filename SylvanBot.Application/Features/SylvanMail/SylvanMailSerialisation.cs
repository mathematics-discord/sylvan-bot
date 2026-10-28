using Microsoft.EntityFrameworkCore;
using SylvanBot.Infrastructure.Data;

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
}