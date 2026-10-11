using RatBot.Infrastructure.Features.Meta;

namespace RatBot.Features.Meta.BackgroundWorkers;

public sealed class MetaProposalPollBackgroundWorker(IServiceScopeFactory scopeFactory, MetaProposalPollResolver pollResolver, ILogger logger)
    : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private readonly ILogger _logger = logger.ForContext<MetaProposalPollBackgroundWorker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("Meta proposal poll background worker started");

        while (!stoppingToken.IsCancellationRequested)
            try
            {
                await ResolveExpiredPollsAsync(stoppingToken);
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Meta proposal poll background worker is stopping");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Meta proposal poll background worker encountered an error");
                await Task.Delay(PollInterval, stoppingToken);
            }
    }

    private async Task ResolveExpiredPollsAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        MetaProposalService service = scope.ServiceProvider.GetRequiredService<MetaProposalService>();

        IReadOnlyList<MetaProposalState> expiredPolls = await service.FindExpiredPollsAsync(DateTimeOffset.UtcNow, BatchSize, ct);

        foreach (MetaProposalState state in expiredPolls)
            await pollResolver.ResolveExpiredPollAsync(service, state, ct);
    }
}
