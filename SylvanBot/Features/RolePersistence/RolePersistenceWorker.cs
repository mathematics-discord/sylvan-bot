using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RolePersistence;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Gateway;

namespace SylvanBot.Features.RolePersistence;

public sealed class RolePersistenceWorker(DiscordSocketClient client, IDbContextFactory<BotDbContext> contexts, ILogger logger)
    : BackgroundService,
        IDiscordGatewayHandler
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new();
    private readonly Channel<(ulong Guild, ulong User, ulong[]? Roles)> _work = Channel.CreateUnbounded<(ulong, ulong, ulong[]?)>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    internal SemaphoreSlim Gate(ulong guild) => _gates.GetOrAdd(guild, _ => new SemaphoreSlim(1, 1));

    public Task InitializeAsync(CancellationToken ct)
    {
        client.UserLeft += LeftAsync;
        client.UserJoined += JoinedAsync;
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.UserLeft -= LeftAsync;
        client.UserJoined -= JoinedAsync;
        _work.Writer.TryComplete();
    }

    private Task LeftAsync(SocketGuild guild, SocketUser user)
    {
        if (user is SocketGuildUser member)
            _work.Writer.TryWrite((guild.Id, user.Id, member.Roles.Select(x => x.Id).ToArray()));
        else
            logger.Warning("Cannot save departing roles: guild {GuildId}, user {UserId} was not cached", guild.Id, user.Id);
        return Task.CompletedTask;
    }

    private Task JoinedAsync(SocketGuildUser user)
    {
        _work.Writer.TryWrite((user.Guild.Id, user.Id, null));
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
        {
            var gate = Gate(work.Guild);
            await gate.WaitAsync(stoppingToken);
            try
            {
                await ProcessAsync(work.Guild, work.User, work.Roles, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Role persistence failed in guild {GuildId} for user {UserId}", work.Guild, work.User);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private async Task ProcessAsync(ulong guildId, ulong userId, ulong[]? departedRoles, CancellationToken ct)
    {
        var guild = client.GetGuild(guildId);
        if (guild is null)
            return;
        await using var db = await contexts.CreateDbContextAsync(ct);
        var pending = await db.PendingRoles.Where(x => x.GuildId == guildId && x.UserId == userId).ToListAsync(ct);
        var configured = await db.PersistentRoles.Where(x => x.GuildId == guildId).Select(x => x.RoleId).ToArrayAsync(ct);
        if (departedRoles is not null)
        {
            var selected = departedRoles
                .Intersect(configured)
                .Where(id => guild.GetRole(id) is { } role && PersistentRolePolicy.IsSafe(guild, role))
                .ToHashSet();
            db.PendingRoles.RemoveRange(pending.Where(x => !selected.Contains(x.RoleId)));
            foreach (var id in selected.Except(pending.Select(x => x.RoleId)))
                db.PendingRoles.Add(
                    new PendingRole
                    {
                        GuildId = guildId,
                        UserId = userId,
                        RoleId = id,
                    }
                );
            await db.SaveChangesAsync(ct);
            return;
        }
        var request = new RequestOptions { CancelToken = ct, AuditLogReason = "Role persistence" };
        var member = await client.Rest.GetGuildUserAsync(guildId, userId, request);
        if (member is null)
            return;
        var sources = await db.RoleColourOptions.Where(x => x.GuildId == guildId).Select(x => x.SourceRoleId).ToArrayAsync(ct);
        foreach (var saved in pending.OrderByDescending(x => sources.Contains(x.RoleId)))
        {
            var role = guild.GetRole(saved.RoleId);
            if (!configured.Contains(saved.RoleId) || role is null || !PersistentRolePolicy.IsSafe(guild, role))
                db.PendingRoles.Remove(saved);
            else if (member.RoleIds.Contains(saved.RoleId))
                db.PendingRoles.Remove(saved);
            else if (!guild.CurrentUser.GuildPermissions.ManageRoles || guild.CurrentUser.Hierarchy <= role.Position)
                logger.Warning("Role persistence blocked by hierarchy or permissions: guild {GuildId}, role {RoleId}", guildId, saved.RoleId);
            else
            {
                try
                {
                    await member.AddRoleAsync(saved.RoleId, request);
                    db.PendingRoles.Remove(saved);
                }
                catch (Discord.Net.HttpException ex)
                {
                    logger.Warning(ex, "Could not restore role {RoleId} for {UserId}", saved.RoleId, userId);
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
