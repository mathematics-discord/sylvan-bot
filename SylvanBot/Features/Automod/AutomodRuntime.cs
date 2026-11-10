using System.Data;
using System.Collections.Concurrent;
using SylvanBot.Application.Features.Tickets;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.Automod;

using DotNext;

public sealed class AutomodRuntime(IDbContextFactory<BotDbContext> contexts, ILogger logger) : IHostedService, IDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly ILogger _logger = logger.ForContext<AutomodRuntime>();
    private ImmutableDictionary<ulong, AutomodConfiguration> _configurations = ImmutableDictionary<ulong, AutomodConfiguration>.Empty;
    private bool _loaded;
    private readonly ConcurrentDictionary<ulong, byte> _closedGuilds = new ConcurrentDictionary<ulong, byte>();

    public bool IsEnabled(ulong guildId) => !_closedGuilds.ContainsKey(guildId)
        && Current(guildId).TryGet(out var configuration) && configuration.Enabled;

    public void CloseGuild(ulong guildId) => _closedGuilds.TryAdd(guildId, 0);

    public void OpenGuild(ulong guildId) => _closedGuilds.TryRemove(guildId, out _);


    public Optional<AutomodConfiguration> Current(ulong guildId)
    {
        if (!Volatile.Read(ref _loaded))
            return default;

        ImmutableDictionary<ulong, AutomodConfiguration> configurations = Volatile.Read(ref _configurations);
        return new Optional<AutomodConfiguration>(configurations.GetValueOrDefault(guildId, AutomodConfiguration.Empty));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            (AutomodRule[] rules, AutomodExemptRole[] roles, AutomodSettings[] settings, TicketConfiguration[] tickets) = await ReadAsync(default, cancellationToken);
            ILookup<ulong, AutomodRule> rulesByGuild = rules.ToLookup(rule => rule.GuildId);
            ILookup<ulong, ulong> rolesByGuild = roles.ToLookup(role => role.GuildId, role => role.RoleId);
            ILookup<ulong, AutomodAction> disabledByGuild = settings
                .Where(setting => !setting.Enabled)
                .ToLookup(setting => setting.GuildId, setting => setting.Action);

            IEnumerable<ulong> guildIds = rulesByGuild
                .Select(group => group.Key)
                .Union(rolesByGuild.Select(group => group.Key))
                .Union(disabledByGuild.Select(group => group.Key))
                .Union(tickets.Select(configuration => configuration.GuildId));

            ImmutableDictionary<ulong, AutomodConfiguration> configurations = guildIds
                .ToImmutableDictionary(id => id, id => new AutomodConfiguration(rulesByGuild[id], rolesByGuild[id], disabledByGuild[id], tickets.Any(configuration => configuration.GuildId == id && configuration.Enabled)));

            Volatile.Write(ref _configurations, configurations);
            Volatile.Write(ref _loaded, true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load automod configuration at startup");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RefreshAsync(ulong guildId, CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct);

            try
            {
                if (!_loaded)
                    throw new InvalidOperationException("Automod startup loading has not completed.");

                (AutomodRule[] rules, AutomodExemptRole[] roles, AutomodSettings[] settings, TicketConfiguration[] tickets) = await ReadAsync(new Optional<ulong>(guildId), ct);
                IEnumerable<ulong> roleIds = roles
                    .Select(role => role.RoleId);

                IEnumerable<AutomodAction> disabledActions = settings
                    .Where(setting => !setting.Enabled)
                    .Select(setting => setting.Action);

                var configuration = new AutomodConfiguration(rules, roleIds, disabledActions, tickets.Any(configuration => configuration.Enabled));
                ImmutableDictionary<ulong, AutomodConfiguration> configurations = rules.Length == 0 && roles.Length == 0 && settings.Length == 0 && tickets.Length == 0
                    ? _configurations.Remove(guildId)
                    : _configurations.SetItem(guildId, configuration);

                Volatile.Write(ref _configurations, configurations);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh automod configuration for guild {GuildId}; retaining previous configuration", guildId);
            return false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _gate.Dispose();

    private async Task<(AutomodRule[] Rules, AutomodExemptRole[] Roles, AutomodSettings[] Settings, TicketConfiguration[] Tickets)> ReadAsync(Optional<ulong> guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        IQueryable<AutomodRule> rulesQuery = db.AutomodRules
            .AsNoTracking();

        IQueryable<AutomodExemptRole> rolesQuery = db.AutomodExemptRoles
            .AsNoTracking();

        IQueryable<AutomodSettings> settingsQuery = db.AutomodSettings
            .AsNoTracking();

        IQueryable<TicketConfiguration> ticketsQuery = db.TicketConfigurations
            .AsNoTracking();

        if (guildId.HasValue)
        {
            var id = guildId.Value;
            ticketsQuery = ticketsQuery
                .Where(configuration => configuration.GuildId == id);

            rulesQuery = rulesQuery
                .Where(rule => rule.GuildId == id);

            rolesQuery = rolesQuery
                .Where(role => role.GuildId == id);

            settingsQuery = settingsQuery
                .Where(settings => settings.GuildId == id);
        }

        AutomodRule[] rules = await rulesQuery
            .ToArrayAsync(ct);

        AutomodExemptRole[] roles = await rolesQuery
            .ToArrayAsync(ct);

        AutomodSettings[] settings = await settingsQuery
            .ToArrayAsync(ct);

        TicketConfiguration[] tickets = await ticketsQuery
            .ToArrayAsync(ct);

        await transaction.CommitAsync(ct);
        return (rules, roles, settings, tickets);
    }
}
