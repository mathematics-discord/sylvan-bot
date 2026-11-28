using Microsoft.EntityFrameworkCore;
using Npgsql;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.Automod;

using DotNext;
using Microsoft.EntityFrameworkCore.Storage;

public sealed class AutomodManagement(IDbContextFactory<BotDbContext> contexts, AutomodRuntime runtime, TimeProvider clock)
{
    public async Task<(ImmutableArray<AutomodRule> Rules, bool RuntimeRefreshed)> AddAsync(
        ulong guildId,
        string input,
        AutomodAction action,
        Optional<string> duration,
        CancellationToken ct)
    {
        ImmutableArray<AutomodPattern> patterns = AutomodInputParser.Parse(input);
        Optional<long> seconds = AutomodDuration.ResolveSeconds(action, duration, clock.GetUtcNow());

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

        await using (BotDbContext db = await contexts.CreateDbContextAsync(ct))
        {
            db.AutomodRules.AddRange(rules);
            await db.SaveChangesAsync(ct);
        }

        var refreshed = await runtime.RefreshAsync(guildId, ct);

        return (rules, refreshed);
    }

    public async Task<(Optional<long> ReplacementId, bool RuntimeRefreshed)> EditAsync(
        ulong guildId,
        long id,
        AutomodAction action,
        Optional<string> duration,
        CancellationToken ct)
    {
        Optional<long> seconds = AutomodDuration.ResolveSeconds(action, duration, clock.GetUtcNow());
        AutomodRule replacement;

        await using (BotDbContext db = await contexts.CreateDbContextAsync(ct))
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);

            if (!await DisableActiveRuleAsync(db, guildId, id, ct))
                return (default, false);

            AutomodRule previous = await db.AutomodRules
                .AsNoTracking()
                .SingleAsync(rule => rule.GuildId == guildId && rule.Id == id, ct);

            replacement = new AutomodRule
            {
                GuildId = guildId,
                Pattern = previous.Pattern,
                MatchType = previous.MatchType,
                Action = action,
                DurationSeconds = seconds.HasValue ? seconds.Value : null,
            };

            db.AutomodRules.Add(replacement);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var refreshed = await runtime.RefreshAsync(guildId, ct);
        return (new Optional<long>(replacement.Id), refreshed);
    }

    public async Task<(bool Disabled, bool RuntimeRefreshed)> DisableAsync(ulong guildId, long id, CancellationToken ct)
    {
        bool disabled;

        await using (BotDbContext db = await contexts.CreateDbContextAsync(ct))
            disabled = await DisableActiveRuleAsync(db, guildId, id, ct);

        // A repeated disable can recover a stale runtime configuration.
        var refreshed = await runtime.RefreshAsync(guildId, ct);
        return (disabled, refreshed);
    }

    public async Task<Optional<AutomodRule>> FindAsync(ulong guildId, long id, CancellationToken ct)
    {
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);

        AutomodRule? rule = await db.AutomodRules
            .AsNoTracking()
            .SingleOrDefaultAsync(rule => rule.GuildId == guildId && rule.Id == id, ct);

        return rule is null ? default : new Optional<AutomodRule>(rule);
    }

    public async Task<ImmutableArray<AutomodRule>> ListAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);

        AutomodRule[] rules = await db.AutomodRules
            .AsNoTracking()
            .Where(rule => rule.GuildId == guildId && !rule.Disabled)
            .OrderBy(rule => rule.Id)
            .ToArrayAsync(ct);

        return rules.ToImmutableArray();
    }

    public async Task<ImmutableArray<AutomodAction>> ListDisabledActionsAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);

        AutomodAction[] actions = await db.AutomodSettings
            .AsNoTracking()
            .Where(settings => settings.GuildId == guildId && !settings.Enabled)
            .Select(settings => settings.Action)
            .ToArrayAsync(ct);

        return actions.ToImmutableArray();
    }

    public async Task<bool> SetEnabledAsync(ulong guildId, Optional<AutomodAction> action, bool enabled, CancellationToken ct)
    {
        if (action.HasValue && !Enum.IsDefined(action.Value))
            throw new ArgumentOutOfRangeException(nameof(action));

        AutomodAction[] actions = action.HasValue ? [action.Value] : Enum.GetValues<AutomodAction>();

        await using (BotDbContext db = await contexts.CreateDbContextAsync(ct))
        {
            Dictionary<AutomodAction, AutomodSettings> existingSettings = await db.AutomodSettings
                .AsTracking()
                .Where(settings => settings.GuildId == guildId && actions.Contains(settings.Action))
                .ToDictionaryAsync(settings => settings.Action, ct);

            foreach (AutomodAction target in actions)
            {
                if (existingSettings.TryGetValue(target, out AutomodSettings? settings))
                {
                    settings.Enabled = enabled;

                    // Write the requested value even if the read returned the same value.
                    db.Entry(settings)
                        .Property(setting => setting.Enabled)
                        .IsModified = true;
                }
                else
                {
                    db.AutomodSettings.Add(new AutomodSettings { GuildId = guildId, Action = target, Enabled = enabled });
                }
            }

            await db.SaveChangesAsync(ct);
        }

        return await runtime.RefreshAsync(guildId, ct);
    }

    public async Task<bool> SetExemptAsync(ulong guildId, ulong roleId, bool exempt, CancellationToken ct)
    {
        await using (BotDbContext db = await contexts.CreateDbContextAsync(ct))
        {
            IQueryable<AutomodExemptRole> roles = db.AutomodExemptRoles
                .Where(role => role.GuildId == guildId && role.RoleId == roleId);

            if (!exempt)
            {
                await roles.ExecuteDeleteAsync(ct);
            }
            else if (!await roles.AnyAsync(ct))
            {
                db.AutomodExemptRoles.Add(new AutomodExemptRole { GuildId = guildId, RoleId = roleId });

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: "PK_automod_exempt_roles",
                })
                {
                    // A concurrent add already made the role exempt.
                }
            }
        }

        return await runtime.RefreshAsync(guildId, ct);
    }

    public async Task<ImmutableArray<ulong>> ListExemptRolesAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);

        var ids = await db.AutomodExemptRoles
            .AsNoTracking()
            .Where(role => role.GuildId == guildId)
            .OrderBy(role => role.RoleId)
            .Select(role => role.RoleId)
            .ToArrayAsync(ct);

        return ids.ToImmutableArray();
    }

    private static async Task<bool> DisableActiveRuleAsync(BotDbContext db, ulong guildId, long id, CancellationToken ct)
    {
        var updated = await db.AutomodRules
            .Where(rule => rule.GuildId == guildId && rule.Id == id && !rule.Disabled)
            .ExecuteUpdateAsync(update => update.SetProperty(rule => rule.Disabled, true), ct);

        return updated > 0;
    }
}
