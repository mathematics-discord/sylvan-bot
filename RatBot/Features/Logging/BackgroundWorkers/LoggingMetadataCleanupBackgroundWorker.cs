using RatBot.Infrastructure.Features.Logging;

namespace RatBot.Features.Logging.BackgroundWorkers;

public sealed class LoggingMetadataCleanupBackgroundWorker(IServiceScopeFactory scopeFactory, IOptions<LoggingOptions> options, ILogger logger)
    : BackgroundService
{
    private readonly ILogger _logger = logger.ForContext<LoggingMetadataCleanupBackgroundWorker>();
    private readonly LoggingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information(
            "Logging metadata cleanup background worker started. RetentionPeriod={RetentionPeriod}, Interval={Interval}",
            _options.MetadataRetentionPeriod,
            _options.MetadataCleanupInterval
        );

        try
        {
            using PeriodicTimer timer = new PeriodicTimer(_options.MetadataCleanupInterval);

            await TryCleanupAsync(stoppingToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await TryCleanupAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Logging metadata cleanup background worker is stopping");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Logging metadata cleanup background worker encountered an error");
        }
    }

    private async Task TryCleanupAsync(CancellationToken ct)
    {
        try
        {
            await CleanupAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Logging metadata cleanup failed; the next scheduled cleanup will retry");
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow - _options.MetadataRetentionPeriod;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ModerationLoggingStore store = scope.ServiceProvider.GetRequiredService<ModerationLoggingStore>();
        int deletedRows = await store.DeleteExpiredMetadataAsync(cutoffUtc, ct).ConfigureAwait(false);

        if (deletedRows > 0)
            _logger.Information("Deleted {DeletedRows} expired logging metadata row(s). CutoffUtc={CutoffUtc}", deletedRows, cutoffUtc);
    }
}
