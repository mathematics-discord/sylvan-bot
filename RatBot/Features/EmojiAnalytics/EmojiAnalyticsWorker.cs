using System.Threading.Channels;

namespace RatBot.Features.EmojiAnalytics;

public sealed class EmojiAnalyticsWorker(
    Channel<EmojiUsageIncrement> buffer,
    IDiscordClient discordClient,
    EmojiUsageStore store,
    IOptions<EmojiAnalyticsOptions> options,
    ILogger logger
) : BackgroundService
{
    private const int BatchSize = 100;
    private readonly ILogger _logger = logger.ForContext<EmojiAnalyticsWorker>();
    private readonly EmojiAnalyticsOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("Emoji analytics background worker started");
        try
        {
            while (await buffer.Reader.WaitToReadAsync(stoppingToken))
            {
                int count = await ProcessNextBatchAsync(stoppingToken);
                if (count < BatchSize)
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.Information("Emoji analytics background worker is stopping");
        }
    }

    internal async Task<int> ProcessNextBatchAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<EmojiUsageIncrement> batch = new List<EmojiUsageIncrement>(BatchSize);
        while (batch.Count < BatchSize && buffer.Reader.TryRead(out EmojiUsageIncrement increment))
            batch.Add(increment);

        foreach (IGrouping<ulong, EmojiUsageIncrement> guildBatch in batch.GroupBy(item => item.GuildId))
        {
            ct.ThrowIfCancellationRequested();
            if (!_options.IsEnabled(guildBatch.Key))
                continue;

            try
            {
                IGuild? guild = await discordClient.GetGuildAsync(guildBatch.Key, CacheMode.CacheOnly,
                    new RequestOptions { CancelToken = ct });
                if (guild is null)
                    continue;

                EmojiUsageIncrement[] increments = guildBatch
                    .GroupBy(item => item.EmojiId)
                    .Select(group => new EmojiUsageIncrement(
                        guildBatch.Key,
                        group.Key,
                        group.Sum(item => item.MessageCount),
                        group.Sum(item => item.ReactionCount)
                    ))
                    .ToArray();
                ulong[] trackedEmojiIds = guild.Emotes.Select(emote => emote.Id).ToArray();
                await store.RecordBatchAsync(guildBatch.Key, increments, trackedEmojiIds, ct);
                _logger.Debug("Processed {Count} emoji usage increments for guild {GuildId}", guildBatch.Count(),
                    guildBatch.Key);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex,
                    "Failed to process emoji analytics batch for guild {GuildId}; discarding this guild's batch",
                    guildBatch.Key);
            }
        }

        return batch.Count;
    }
}