using static Discord.ConnectionState;

namespace SylvanBot.Features.Automod;

public sealed class AutomodBanWorker(DiscordSocketClient client, AutomodBanEnforcement enforcement, ILogger logger)
    : BackgroundService, SylvanBot.Gateway.IDiscordGatewayHandler
{
    private bool _ready;

    public Task InitializeAsync(CancellationToken ct)
    {
        client.Ready += ReadyAsync;

        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        _ready = true;

        return Task.CompletedTask;
    }

    public void Unsubscribe() => client.Ready -= ReadyAsync;

    public override async Task StopAsync(CancellationToken ct)
    {
        Unsubscribe();
        await base.StopAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_ready && client is { ConnectionState: Connected, CurrentUser: not null })
                {
                    try
                    {
                        await enforcement.RecoverAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Automod ban recovery pass failed");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}