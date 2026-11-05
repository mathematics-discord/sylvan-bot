namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Gateway;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketWorker(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contextFactory,
    TicketCoordinator coordinator,
    TicketBoundary boundary,
    ILogger logger) : BackgroundService, IDiscordGatewayHandler
{
    private readonly SemaphoreSlim _wake = new SemaphoreSlim(0, 1);
    private readonly ILogger _logger = logger.ForContext<TicketWorker>();
    private bool _ready;

    public Task InitializeAsync(CancellationToken ct)
    {
        client.Ready += OnReadyAsync;
        client.AuditLogCreated += OnAuditAsync;
        client.UserVoiceStateUpdated += OnVoiceAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.Ready -= OnReadyAsync;
        client.AuditLogCreated -= OnAuditAsync;
        client.UserVoiceStateUpdated -= OnVoiceAsync;
    }

    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending wake-up already covers the durable work.
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _wake.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);

                if (!_ready || client.ConnectionState != ConnectionState.Connected)
                    continue;

                try
                {
                    await ProcessGuildsAsync(stoppingToken);
                    await ProcessModeratorsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Error("Ticket recovery pass failed ({FailureType})", exception.GetType().Name);
                }

                // Coalesce bursts without keeping gateway callbacks occupied.
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.Information("Ticket worker stopped");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }

    private Task OnVoiceAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        Wake();
        return Task.CompletedTask;
    }

    private Task OnReadyAsync()
    {
        _ready = true;
        Wake();

        return Task.CompletedTask;
    }

    private Task OnAuditAsync(SocketAuditLogEntry entry, SocketGuild guild)
    {
        Wake();

        return Task.CompletedTask;
    }

    private async Task ProcessGuildsAsync(CancellationToken token)
    {
        ulong cursor = 0;

        while (true)
        {
            await using var db = await contextFactory.CreateDbContextAsync(token);

            var afterCursor = cursor;

            List<TicketConfiguration> configurations = await db.TicketConfigurations
                .AsNoTracking()
                .Where(config => config.GuildId > afterCursor
                                 && config.Enabled
                                 && config.DestinationChannelId != null
                                 && config.CaptureFromUtc != null)
                .OrderBy(config => config.GuildId)
                .Take(100)
                .ToListAsync(token);

            if (configurations.Count == 0)
                return;

            foreach (var config in configurations)
            {
                var auditCurrent = false;

                try
                {
                    var started = DateTimeOffset.UtcNow;
                    var from = config.LastAuditScanAtUtc ?? config.CaptureFromUtc!.Value;

                    if (from < started.AddDays(-45))
                        await coordinator.SuspendRestrictionsAsync(config.GuildId, token);

                    var scan = await boundary.ReadAuditAsync(config.GuildId, from.AddMinutes(-10), token);
                    await coordinator.AcceptScanAsync(config, scan, started, token);
                    auditCurrent = true;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Warning(
                        "Audit recovery failed in guild {GuildId} ({FailureType}); expiry is paused for this pass",
                        config.GuildId,
                        exception.GetType().Name);
                }

                long after = 0;

                while (true)
                {
                    var now = DateTimeOffset.UtcNow;

                    List<long> ids = await db.Tickets
                        .AsNoTracking()
                        .Where(ticket => ticket.GuildId == config.GuildId
                                         && ticket.Id > after
                                         && (ticket.Pending != null
                                             || (ticket.Status == TicketStatus.Active && !ticket.ExpirySuspended && ticket.ExpiresAtUtc <= now)
                                             || (!ticket.Hidden
                                                 && !ticket.SendUncertain
                                                 && !ticket.Publications.Any(reference => reference.Current && !reference.Removed && reference.Revision == ticket.Version))
                                             || ticket.Publications.Any(reference => !reference.Removed && (ticket.Hidden || !reference.Current))))
                        .OrderBy(ticket => ticket.Id)
                        .Select(ticket => ticket.Id)
                        .Take(100)
                        .ToListAsync(token);

                    if (ids.Count == 0)
                        break;

                    foreach (var id in ids)
                    {
                        try
                        {
                            await coordinator.RecoverAsync(config.GuildId, id, auditCurrent, token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            _logger.Warning(
                                "Ticket recovery failed for {TicketId} in guild {GuildId} ({FailureType})",
                                id,
                                config.GuildId,
                                exception.GetType().Name);
                        }
                    }

                    after = ids[^1];
                }
            }

            cursor = configurations[^1].GuildId;
        }
    }

    private async Task ProcessModeratorsAsync(CancellationToken token)
    {
        ulong after = 0;

        while (true)
        {
            await using var db = await contextFactory.CreateDbContextAsync(token);

            IQueryable<ulong> enabled = db.TicketConfigurations
                .Where(config => config.Enabled && config.DestinationChannelId != null)
                .Select(config => config.GuildId);

            IQueryable<ulong> moderators = db.TicketModerators
                .Where(moderator => moderator.ActiveTicketId != null)
                .Select(moderator => moderator.UserId);

            List<ulong> users = await db.Tickets
                .Where(ticket => !ticket.Hidden
                                 && !ticket.ReminderExempt
                                 && string.IsNullOrWhiteSpace(ticket.Comment)
                                 && enabled.Contains(ticket.GuildId))
                .Select(ticket => ticket.ModeratorUserId)
                .Union(moderators)
                .Where(user => user > after)
                .OrderBy(user => user)
                .Take(100)
                .ToListAsync(token);

            if (users.Count == 0)
                return;

            foreach (var user in users)
            {
                try
                {
                    await coordinator.RemindAsync(user, force: false, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Warning("Ticket reminder failed for moderator {ModeratorId} ({FailureType})", user, exception.GetType().Name);
                }
            }

            after = users[^1];
        }
    }
}