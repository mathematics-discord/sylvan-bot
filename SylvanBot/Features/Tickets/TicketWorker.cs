namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Gateway;
using SylvanBot.Application.Infrastructure.Data;

public sealed partial class TicketWorker(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contextFactory,
    TicketWorkflow workflow,
    IDiscordTicket discord,
    ILogger logger) : BackgroundService, IDiscordGatewayHandler
{
    private readonly SemaphoreSlim _wake = new SemaphoreSlim(0, 1);
    private readonly ILogger _logger = logger.ForContext<TicketWorker>();
    private bool _ready;

    public Task InitializeAsync(CancellationToken ct)
    {
        client.Ready += OnReadyAsync;
        client.AuditLogCreated += OnAuditAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.Ready -= OnReadyAsync;
        client.AuditLogCreated -= OnAuditAsync;
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

    private static RequestOptions Options(CancellationToken cancellationToken)
        => new RequestOptions
        {
            CancelToken = cancellationToken,
            RetryMode = RetryMode.AlwaysFail,
        };
}