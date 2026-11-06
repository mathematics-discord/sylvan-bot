using System.Linq.Expressions;
using Discord.Net;
using Discord.Rest;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Application.Infrastructure.Data;
using static System.Net.HttpStatusCode;
using static System.StringComparison;

namespace SylvanBot.Features.Automod;

public sealed class AutomodBanEnforcement(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    TimeProvider clock,
    ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    public async Task BanAsync(ulong guildId, ulong userId, ulong messageId, AutomodRule rule, string reason, CancellationToken ct)
    {
        DotNext.Optional<long> seconds = rule.DurationSeconds is { } duration ? new DotNext.Optional<long>(duration) : default;
        DotNext.Optional<DateTimeOffset> expiry = AutomodDuration.BanExpiry(seconds, clock.GetUtcNow());

        await _gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);

            var outstanding = await db.AutomodBans
                .AnyAsync(OutstandingBanFor(guildId, userId), ct);

            if (outstanding)
                return;

            var guild = await client.Rest.GetGuildAsync(guildId, Options(ct));

            if (await guild.GetBanAsync(userId, Options(ct)) is not null)
                return;

            var now = clock.GetUtcNow();

            var operation = new AutomodBan
            {
                Id = Guid.NewGuid(), GuildId = guildId, UserId = userId,
                MessageId = messageId, RuleId = rule.Id, RequestedAt = now,
                ActionReason = reason,
                CheckedAt = now, NextCheckAt = now.AddSeconds(30), LastAttemptAt = now, ExpiresAt = expiry,
            };
            db.AutomodBans.Add(operation);
            await db.SaveChangesAsync(ct);

            try
            {
                await guild.AddBanAsync(userId, pruneDays: 0, reason: operation.Reason, options: Options(ct));
            }
            catch (HttpException ex) when (ex.HttpCode is BadRequest or Forbidden or NotFound)
            {
                operation.Status = "Rejected";
                await db.SaveChangesAsync(ct);

                logger.Warning(
                    "Automod ban rejected in guild {GuildId}, user {UserId}, operation {OperationId}: {Code}",
                    guildId,
                    userId,
                    operation.Id,
                    ex.DiscordCode);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var now = clock.GetUtcNow();

            List<AutomodBan> records = await db.AutomodBans
                .Where(DueForRecovery(now))
                .OrderBy(value => value.NextCheckAt)
                .Take(100)
                .ToListAsync(ct);

            foreach (var record in records)
            {
                try
                {
                    await ReconcileAsync(db, record, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.Warning(
                        ex,
                        "Automod ban recovery paused in guild {GuildId}, user {UserId}, operation {OperationId}",
                        record.GuildId,
                        record.UserId,
                        record.Id);
                }
                finally
                {
                    record.NextCheckAt = clock.GetUtcNow().AddSeconds(30);
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReconcileAsync(BotDbContext db, AutomodBan record, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        if (record.AuditCoverageLost(now))
        {
            record.Status = "Suspended";
            await db.SaveChangesAsync(ct);

            logger.Warning(
                "Automod ban expiry suspended: audit gap requires manual review in guild {GuildId}, user {UserId}, operation {OperationId}",
                record.GuildId,
                record.UserId,
                record.Id);

            return;
        }

        var guild = await client.Rest.GetGuildAsync(record.GuildId);
        var boundary = record.CheckedAt.AddMinutes(-10);
        List<RestAuditLogEntry> entries = new List<RestAuditLogEntry>();

        await foreach (IReadOnlyCollection<RestAuditLogEntry> page in guild.GetAuditLogsAsync(limit: int.MaxValue).WithCancellation(ct))
        {
            var unreadable = page.Any(HasUnreadableBanData);

            if (unreadable)
                throw new InvalidDataException("Unreadable ban audit history.");

            entries.AddRange(page);

            if (page.Any(entry => entry.CreatedAt < boundary))
                break;
        }

        var requestedAfter = record.RequestedAt.AddSeconds(-1);

        List<RestAuditLogEntry> changes = entries
            .Where(BanChangesFor(record.UserId, requestedAfter))
            .OrderBy(entry => entry.Id)
            .ToList();

        foreach (var change in changes)
        {
            if (change.Id <= record.AuditId)
                continue;

            var ownBan = change.Data is BanAuditLogData
                         && change.User.Id == client.CurrentUser.Id
                         && string.Equals(change.Reason, record.Reason, OrdinalIgnoreCase);

            var ownRemoval = change.Data is UnbanAuditLogData
                             && change.User.Id == client.CurrentUser.Id
                             && string.Equals(
                                 change.Reason,
                                 $"Automod ban expiry rule {record.RuleId}; automod operation {record.Id:D}",
                                 OrdinalIgnoreCase);

            record.Observe(change.Id, ownBan, ownRemoval);

            if (record.Status is not ("Superseded" or "Ended"))
                continue;

            await db.SaveChangesAsync(ct);

            return;
        }

        var live = await guild.GetBanAsync(record.UserId, Options(ct));

        if (live is null)
        {
            record.Status = "Ended";
        }
        else if (record.AuditId != 0 && !string.Equals(live.Reason, record.Reason, OrdinalIgnoreCase))
        {
            record.Status = "Superseded";
        }
        else if (record.AuditId != 0 && !record.ExpiresAt.HasValue)
        {
            record.Status = "Permanent";
        }
        else if (record.CanExpire(now))
        {
            record.Status = "Removing";
            record.LastAttemptAt = now;

            await db.SaveChangesAsync(ct);

            var options = Options(ct);
            options.AuditLogReason = $"Automod ban expiry rule {record.RuleId}; automod operation {record.Id:D}";

            await guild.RemoveBanAsync(record.UserId, options);

            record.Status = "Ended";
        }

        if (record.AuditId != 0)
        {
            record.CheckedAt = now;
        }
        else if (string.Equals(record.Status, "Pending", Ordinal))
        {
            logger.Warning(
                "Automod ban awaits audit confirmation in guild {GuildId}, user {UserId}, operation {OperationId}",
                record.GuildId,
                record.UserId,
                record.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    private static Expression<Func<AutomodBan, bool>> OutstandingBanFor(ulong guildId, ulong userId) =>
        value => value.GuildId == guildId
                 && value.UserId == userId
                 && (value.Status == "Pending" || value.Status == "Active" || value.Status == "Removing");

    private static Expression<Func<AutomodBan, bool>> DueForRecovery(DateTimeOffset now) =>
        value => (value.Status == "Pending" || value.Status == "Active" || value.Status == "Removing") && value.NextCheckAt <= now;

    private static bool HasUnreadableBanData(RestAuditLogEntry entry) =>
        entry is { Action: ActionType.Ban, Data: not BanAuditLogData } or { Action: ActionType.Unban, Data: not UnbanAuditLogData };

    private static Func<RestAuditLogEntry, bool> BanChangesFor(ulong userId, DateTimeOffset requestedAfter) =>
        entry => entry.CreatedAt >= requestedAfter
                 && (entry.Data is BanAuditLogData ban && ban.Target.Id == userId
                     || entry.Data is UnbanAuditLogData unban && unban.Target.Id == userId);

    private static RequestOptions Options(CancellationToken ct) => new RequestOptions
    {
        CancelToken = ct, RetryMode = RetryMode.AlwaysFail,
    };

    public void Dispose() => _gate.Dispose();
}
