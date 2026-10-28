using SylvanBot.Application.Features.MessageCache;

namespace SylvanBot.Features.MessageCache;

public sealed class MessageCacheMaintenanceWorker(MessageSnapshotCache cache, ILogger logger) : BackgroundService
{
    private readonly ILogger _logger = logger.ForContext<MessageCacheMaintenanceWorker>();
    private long _failures;

    public long Failures => Interlocked.Read(ref _failures);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!cache.Ready)
                    continue;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                try
                {
                    await cache.PruneAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref _failures);
                    _logger.Warning("Message-cache maintenance failed: {ExceptionType}", propertyValue: exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}