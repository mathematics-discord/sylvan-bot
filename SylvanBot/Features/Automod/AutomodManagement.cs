using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.Automod;

public sealed class AutomodManagement(IDbContextFactory<BotDbContext> contexts, AutomodRuntime runtime)
{
    public async Task<(ImmutableArray<AutomodRule> Rules, bool RuntimeRefreshed)> AddAsync(
        ulong guildId,
        string input,
        AutomodAction action,
        DotNext.Optional<string> duration,
        CancellationToken ct)
    {
        ImmutableArray<AutomodPattern> patterns = AutomodInputParser.Parse(input);
        DotNext.Optional<long> seconds = AutomodDuration.ResolveSeconds(action, duration);

        ImmutableArray<AutomodRule> rules = patterns
            .Select(pattern => new AutomodRule
            {
                GuildId = guildId,
                Pattern = pattern.Pattern,
                MatchType = pattern.MatchType,
                Action = action,
                DurationSeconds = seconds.HasValue ? seconds.Value : null,
            })
            .ToImmutableArray();

        await using (var db = await contexts.CreateDbContextAsync(ct))
        {
            db.AutomodRules.AddRange(rules);
            await db.SaveChangesAsync(ct);
        }

        var refreshed = await runtime.RefreshAsync(guildId, ct);
        return (rules, refreshed);
    }

    public async Task<(bool Removed, bool RuntimeRefreshed)> RemoveAsync(ulong guildId, long id, CancellationToken ct)
    {
        int removed;

        await using (var db = await contexts.CreateDbContextAsync(ct))
        {
            removed = await db.AutomodRules
                .Where(rule => rule.GuildId == guildId && rule.Id == id)
                .ExecuteDeleteAsync(ct);
        }

        // Refresh even when already absent so a repeated removal can recover a stale snapshot.
        var refreshed = await runtime.RefreshAsync(guildId, ct);
        return (removed > 0, refreshed);
    }

    public async Task<ImmutableArray<AutomodRule>> ListAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        AutomodRule[] rules = await db.AutomodRules
            .AsNoTracking()
            .Where(rule => rule.GuildId == guildId)
            .OrderBy(rule => rule.Id)
            .ToArrayAsync(ct);

        return rules.ToImmutableArray();
    }

    public async Task<ImmutableArray<AutomodAction>> ListDisabledActionsAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        AutomodAction[] actions = await db.AutomodSettings
            .AsNoTracking()
            .Where(settings => settings.GuildId == guildId && !settings.Enabled)
            .Select(settings => settings.Action)
            .ToArrayAsync(ct);

        return actions.ToImmutableArray();
    }

    public async Task<bool> SetEnabledAsync(ulong guildId, AutomodAction action, bool enabled, CancellationToken ct)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action));

        await using (var db = await contexts.CreateDbContextAsync(ct))
        {
            var storedGuildId = unchecked((long)guildId);
            var storedAction = (int)action;

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO automod_settings ("GuildId", "Action", "Enabled") VALUES ({storedGuildId}, {storedAction}, {enabled})
                ON CONFLICT ("GuildId", "Action") DO UPDATE SET "Enabled" = EXCLUDED."Enabled"
                """,
                ct);
        }

        return await runtime.RefreshAsync(guildId, ct);
    }

    public async Task<bool> SetExemptAsync(ulong guildId, ulong roleId, bool exempt, CancellationToken ct)
    {
        await using (var db = await contexts.CreateDbContextAsync(ct))
        {
            if (exempt)
            {
                // Match the context's ulong-to-bigint conversion and tolerate concurrent duplicate requests.
                var storedGuildId = unchecked((long)guildId);
                var storedRoleId = unchecked((long)roleId);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""INSERT INTO automod_exempt_roles ("GuildId", "RoleId") VALUES ({storedGuildId}, {storedRoleId
                    }) ON CONFLICT ("GuildId", "RoleId") DO NOTHING""",
                    ct);
            }
            else
            {
                await db.AutomodExemptRoles
                    .Where(role => role.GuildId == guildId && role.RoleId == roleId)
                    .ExecuteDeleteAsync(ct);
            }
        }

        return await runtime.RefreshAsync(guildId, ct);
    }

    public async Task<ImmutableArray<ulong>> ListExemptRolesAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var ids = await db.AutomodExemptRoles
            .AsNoTracking()
            .Where(role => role.GuildId == guildId)
            .OrderBy(role => role.RoleId)
            .Select(role => role.RoleId)
            .ToArrayAsync(ct);

        return ids.ToImmutableArray();
    }
}