using System.Threading.Channels;
using SylvanBot.Features.EmojiAnalytics.Persistence;

namespace SylvanBot.Features.EmojiAnalytics;

public sealed class EmojiAnalyticsWorker(
    Channel<EmojiUsageIncrement> buffer,
    IDiscordClient discordClient,
    EmojiUsageStore store,
    IOptions<EmojiAnalyticsOptions> options,
    ILogger logger) : BackgroundService
{
    private const    int                   BatchSize = 100;
    private readonly ILogger               _logger   = logger.ForContext<EmojiAnalyticsWorker>();
    private readonly EmojiAnalyticsOptions _options  = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("Emoji analytics background worker started");

        try
        {
            while (await buffer.Reader.WaitToReadAsync(stoppingToken))
            {
                var count = await ProcessNextBatchAsync(stoppingToken);

                if (count < BatchSize)
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken: stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.Information("Emoji analytics background worker is stopping");
        }
    }

    private async Task<int> ProcessNextBatchAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<EmojiUsageIncrement> batch = new(BatchSize);

        while (batch.Count < BatchSize && buffer.Reader.TryRead(out var increment))
            batch.Add(increment);

        foreach (IGrouping<ulong, EmojiUsageIncrement> guildBatch in batch.GroupBy(item => item.GuildId))
        {
            ct.ThrowIfCancellationRequested();

            if (!_options.IsEnabled(guildBatch.Key))
                continue;

            try
            {
                var guild = await discordClient.GetGuildAsync(
                    id: guildBatch.Key,
                    mode: CacheMode.CacheOnly,
                    new RequestOptions { CancelToken = ct });

                if (guild is null)
                    continue;

                EmojiUsageIncrement[] increments = guildBatch
                   .GroupBy(item => item.EmojiId)
                   .Select(group => new EmojiUsageIncrement(
                               GuildId: guildBatch.Key,
                               EmojiId: group.Key,
                               group.Sum(item => item.MessageCount),
                               group.Sum(item => item.ReactionCount)))
                   .ToArray();
                var trackedEmojiIds = guild.Emotes.Select(emote => emote.Id).ToArray();
                await store.RecordBatchAsync(guildId: guildBatch.Key, increments: increments, trackedEmojiIds: trackedEmojiIds, ct: ct);

                _logger.Debug(
                    "Processed {Count} emoji usage increments for guild {GuildId}",
                    guildBatch.Count(),
                    propertyValue1: guildBatch.Key);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    exception: ex,
                    "Failed to process emoji analytics batch for guild {GuildId}; discarding this guild's batch",
                    propertyValue: guildBatch.Key);
            }
        }

        return batch.Count;
    }
}