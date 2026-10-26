namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursWorker(
    DiscordSocketClient client,
    WorkingHoursCoordinator coordinator,
    ILogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var guild in client.Guilds)
                {
                    try
                    {
                        await coordinator.ReconcileAsync(guild.Id, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Working Hours pass failed for guild {GuildId}", guild.Id);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}