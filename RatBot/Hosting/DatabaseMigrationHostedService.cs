using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Hosting;

public sealed class DatabaseMigrationHostedService(IServiceScopeFactory scopeFactory, ILogger logger) : IHostedService
{
    private readonly ILogger _logger = logger.ForContext<DatabaseMigrationHostedService>();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        List<string> pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pendingMigrations.Count == 0)
            return;

        _logger.Information("Applying {PendingMigrationCount} pending database migration(s)", pendingMigrations.Count);
        await dbContext.Database.MigrateAsync(cancellationToken);
        _logger.Information("Database migrations applied successfully");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
