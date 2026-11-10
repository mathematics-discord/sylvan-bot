using System.Linq.Expressions;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Application.Infrastructure.Data;
using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Automod;

public sealed class AutomodBanEnforcement(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    AutomodRuntime runtime,
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

            if (!runtime.IsEnabled(guildId))
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

    private static Expression<Func<AutomodBan, bool>> OutstandingBanFor(ulong guildId, ulong userId) =>
        value => value.GuildId == guildId
                 && value.UserId == userId
                 && (value.Status == "Pending" || value.Status == "Active" || value.Status == "Removing");

    private static RequestOptions Options(CancellationToken ct) => new RequestOptions
    {
        CancelToken = ct, RetryMode = RetryMode.AlwaysFail,
    };

    public void Dispose() => _gate.Dispose();
}
