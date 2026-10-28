using System.Diagnostics;
using System.Globalization;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Features.Logging;

namespace SylvanBot.Features.MessageCache;

public sealed class MessageCacheSamplingWorker(
    DiscordSocketClient client,
    MessageSnapshotCache cache,
    ModerationLoggingGatewayHandler loggingHandler,
    MessageCacheMaintenanceWorker maintenance,
    ILogger logger
) : BackgroundService
{
    private readonly ILogger _logger = logger.ForContext<MessageCacheSamplingWorker>();
    private readonly string _processRunId = Guid.NewGuid().ToString("N");
    private volatile bool _initiallyReady;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += OnReadyAsync;

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!cache.Ready)
                    continue;

                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));

                    if (_initiallyReady && client.ConnectionState == ConnectionState.Connected)
                        await SampleAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Message-cache sampling failed for run {ProcessRunId}: {ExceptionType}", _processRunId, ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private static ChannelMeasurement CollectChannel(
        ulong guildId,
        ISocketMessageChannel channel,
        IReadOnlyDictionary<(ulong GuildId, ulong ChannelId), CachedChannelStatistics> statistics,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        statistics.TryGetValue((guildId, channel.Id), out var measurement);

        return new ChannelMeasurement(
            guildId.ToString(CultureInfo.InvariantCulture),
            channel.Id.ToString(CultureInfo.InvariantCulture),
            CachedMessageCount: measurement?.Count ?? 0,
            OldestMessageCreatedAtUtc: measurement?.OldestCreatedAtUtc,
            OldestMessageExpiresAtUtc: measurement?.OldestExpiresAtUtc
        );
    }

    private Task OnReadyAsync()
    {
        _initiallyReady = true;

        return Task.CompletedTask;
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        ImmutableArray<CachedChannelStatistics> statistics = await cache.GetStatisticsAsync(cancellationToken);

        Dictionary<(ulong GuildId, ulong ChannelId), CachedChannelStatistics> byChannel = statistics.ToDictionary(statistic =>
            (statistic.GuildId, statistic.ChannelId)
        );

        Queue<ChannelMeasurement> measurements = [];
        long cachedGuildMembers = 0;

        foreach (var guild in client.Guilds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (client.ConnectionState != ConnectionState.Connected)
                return;

            if (!((IGuild)guild).Available)
                continue;

            cachedGuildMembers += guild.Users.Count;

            IEnumerable<ISocketMessageChannel> channels = guild
                .Channels.Concat(guild.ThreadChannels)
                .DistinctBy(channel => channel.Id)
                .OfType<ISocketMessageChannel>();

            foreach (var channel in channels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (client.ConnectionState != ConnectionState.Connected || !((IGuild)guild).Available)
                    return;

                measurements.Enqueue(
                    CollectChannel(guildId: guild.Id, channel: channel, statistics: byChannel, cancellationToken: cancellationToken)
                );
            }
        }

        if (measurements.Count == 0)
            return;

        LogSnapshot(channels: measurements, cachedGuildMembers: cachedGuildMembers, cancellationToken: cancellationToken);
    }

    private void LogSnapshot(Queue<ChannelMeasurement> channels, long cachedGuildMembers, CancellationToken cancellationToken)
    {
        var populatedChannels = channels.Count(channel => channel.CachedMessageCount > 0);
        var totalCachedMessages = channels.Sum(channel => (long)channel.CachedMessageCount);

        using var process = Process.GetCurrentProcess();
        var workingSetBytes = process.WorkingSet64;
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var collectedAtUtc = DateTimeOffset.UtcNow.ToString("u", formatProvider: CultureInfo.InvariantCulture);

        cancellationToken.ThrowIfCancellationRequested();

        if (client.ConnectionState != ConnectionState.Connected)
            return;

        var snapshotLogger = _logger
            .ForContext("ProcessRunId", value: _processRunId)
            .ForContext("CollectedAtUtc", value: collectedAtUtc)
            .ForContext("RetentionHours", value: 24)
            .ForContext("CacheReady", value: cache.Ready)
            .ForContext("HandlingFailures", value: loggingHandler.Failures)
            .ForContext("MaintenanceFailures", value: maintenance.Failures)
            .ForContext("SampledChannelCount", value: channels.Count)
            .ForContext("PopulatedChannelCount", value: populatedChannels)
            .ForContext("TotalCachedMessages", value: totalCachedMessages)
            .ForContext("CachedGuildMemberCount", value: cachedGuildMembers)
            .ForContext("ProcessWorkingSetBytes", value: workingSetBytes)
            .ForContext("ProcessUptime", value: uptime);

        snapshotLogger.ForContext("MessageCacheSample", value: true).Debug("Message-cache snapshot: {@Channels}", propertyValue: channels);

        snapshotLogger.Debug(
            "Message-cache summary at {CollectedAtUtc}, run {ProcessRunId}: {SampledChannelCount} channels, {PopulatedChannelCount} populated, {TotalCachedMessages} cached messages, {CachedGuildMemberCount} cached guild members, whole-process working set {ProcessWorkingSetBytes} bytes, uptime {ProcessUptime}",
            collectedAtUtc,
            _processRunId,
            channels.Count,
            populatedChannels,
            totalCachedMessages,
            cachedGuildMembers,
            workingSetBytes,
            uptime
        );
    }

    [UsedImplicitly]
    private sealed record ChannelMeasurement(
        string GuildId,
        string ChannelId,
        int CachedMessageCount,
        DateTimeOffset? OldestMessageCreatedAtUtc,
        DateTimeOffset? OldestMessageExpiresAtUtc
    );
}
