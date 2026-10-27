using System.Diagnostics;
using System.Globalization;
using SylvanBot.Gateway;

namespace SylvanBot.Features.MessageCache;

public sealed class MessageCacheSamplingWorker(
    DiscordSocketClient client,
    IOptions<DiscordOptions> options,
    ILogger logger) : BackgroundService
{
    private readonly ILogger _logger       = logger.ForContext<MessageCacheSamplingWorker>();
    private readonly string  _processRunId = Guid.NewGuid().ToString("N");
    private readonly int     _capacity     = options.Value.MessageCacheSize;
    private volatile bool    _initiallyReady;

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
                if (!_initiallyReady || client.ConnectionState != ConnectionState.Connected)
                    continue;

                try
                {
                    Sample(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(exception: ex, "Message-cache sampling pass failed for run {ProcessRunId}", propertyValue: _processRunId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static ChannelMeasurement CollectChannel(
        ulong guildId,
        ISocketMessageChannel channel,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SocketMessage> messages                  = channel.CachedMessages;
        DateTimeOffset?                    oldestMessageCreatedAtUtc = null;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (oldestMessageCreatedAtUtc is null || message.Timestamp < oldestMessageCreatedAtUtc)
                oldestMessageCreatedAtUtc = message.Timestamp.ToUniversalTime();
        }

        return new ChannelMeasurement(
            guildId.ToString(CultureInfo.InvariantCulture),
            channel.Id.ToString(CultureInfo.InvariantCulture),
            CachedMessageCount: messages.Count,
            OldestMessageCreatedAtUtc: oldestMessageCreatedAtUtc);
    }

    private Task OnReadyAsync()
    {
        _initiallyReady = true;

        return Task.CompletedTask;
    }

    private void Sample(CancellationToken cancellationToken)
    {
        Queue<ChannelMeasurement> measurements       = [];
        long                      cachedGuildMembers = 0;

        foreach (var guild in client.Guilds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (client.ConnectionState != ConnectionState.Connected)
                return;

            if (!((IGuild)guild).Available)
                continue;

            cachedGuildMembers += guild.Users.Count;

            IEnumerable<ISocketMessageChannel> channels = guild.Channels
               .Concat(guild.ThreadChannels)
               .DistinctBy(channel => channel.Id)
               .OfType<ISocketMessageChannel>();

            foreach (var channel in channels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (client.ConnectionState != ConnectionState.Connected || !((IGuild)guild).Available)
                    return;

                measurements.Enqueue(CollectChannel(guildId: guild.Id, channel: channel, cancellationToken: cancellationToken));
            }
        }

        if (measurements.Count == 0)
            return;

        LogSnapshot(channels: measurements, cachedGuildMembers: cachedGuildMembers, cancellationToken: cancellationToken);
    }

    private void LogSnapshot(Queue<ChannelMeasurement> channels, long cachedGuildMembers, CancellationToken cancellationToken)
    {
        var populatedChannels   = channels.Count(channel => channel.CachedMessageCount > 0);
        var fullChannels        = channels.Count(channel => _capacity > 0 && channel.CachedMessageCount >= _capacity);
        var totalCachedMessages = channels.Sum(channel => (long)channel.CachedMessageCount);

        using var process         = Process.GetCurrentProcess();
        var       workingSetBytes = process.WorkingSet64;
        var       uptime          = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var       collectedAtUtc  = DateTimeOffset.UtcNow.ToString("u", formatProvider: CultureInfo.InvariantCulture);

        cancellationToken.ThrowIfCancellationRequested();

        if (client.ConnectionState != ConnectionState.Connected)
            return;

        var snapshotLogger = _logger
           .ForContext("ProcessRunId", value: _processRunId)
           .ForContext("CollectedAtUtc", value: collectedAtUtc)
           .ForContext("Capacity", value: _capacity)
           .ForContext("SampledChannelCount", value: channels.Count)
           .ForContext("PopulatedChannelCount", value: populatedChannels)
           .ForContext("FullChannelCount", value: fullChannels)
           .ForContext("TotalCachedMessages", value: totalCachedMessages)
           .ForContext("CachedGuildMemberCount", value: cachedGuildMembers)
           .ForContext("ProcessWorkingSetBytes", value: workingSetBytes)
           .ForContext("ProcessUptime", value: uptime);

        snapshotLogger
           .ForContext("MessageCacheSample", value: true)
           .Debug("Message-cache snapshot: {@Channels}", propertyValue: channels);

        snapshotLogger.Debug(
            "Message-cache summary at {CollectedAtUtc}, run {ProcessRunId}: {SampledChannelCount} channels, {PopulatedChannelCount} populated, {FullChannelCount} full, {TotalCachedMessages} cached messages, {CachedGuildMemberCount} cached guild members, whole-process working set {ProcessWorkingSetBytes} bytes, uptime {ProcessUptime}",
            collectedAtUtc,
            _processRunId,
            channels.Count,
            populatedChannels,
            fullChannels,
            totalCachedMessages,
            cachedGuildMembers,
            workingSetBytes,
            uptime);
    }

    [UsedImplicitly]
    private sealed record ChannelMeasurement(
        string GuildId,
        string ChannelId,
        int CachedMessageCount,
        DateTimeOffset? OldestMessageCreatedAtUtc);
}