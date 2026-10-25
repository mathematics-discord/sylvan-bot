namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;

public sealed partial class TicketWorker
{
    private async Task ProcessGuildsAsync(CancellationToken cancellationToken)
    {
        ulong afterGuildId = 0;

        while (true)
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            List<TicketConfiguration> configurations = await dbContext.TicketConfigurations
                .AsNoTracking()
                .Where(configuration => configuration.GuildId > afterGuildId && configuration.CaptureFromUtc != null)
                .OrderBy(configuration => configuration.GuildId)
                .Take(100)
                .ToListAsync(cancellationToken);

            if (configurations.Count == 0)
                return;

            foreach (var configuration in configurations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var auditCurrent = await workflow.ReconcileAuditAsync(configuration.GuildId, cancellationToken);

                try
                {
                    await workflow.RecoverGuildAsync(configuration.GuildId, auditCurrent, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Warning("Ticket recovery failed in guild {GuildId} ({FailureType})",
                        configuration.GuildId, exception.GetType().Name);
                }
            }

            afterGuildId = configurations[^1].GuildId;
        }
    }
}
