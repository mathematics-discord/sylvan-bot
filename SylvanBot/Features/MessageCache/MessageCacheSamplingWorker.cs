namespace SylvanBot.Features.MessageCache;

using System.Diagnostics;
using System.Globalization;
using SylvanBot.Gateway;

public sealed class MessageCacheSamplingWorker(
    DiscordSocketClient client,
    IOptions<DiscordOptions> options,
    ILogger logger) : BackgroundService
{
    private readonly ILogger _logger = logger.ForContext<MessageCacheSamplingWorker>();
    private readonly string _processRunId = Guid.NewGuid().ToString("N");
    private readonly int _capacity = options.Value.MessageCacheSize;
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
                    _logger.Error(ex, "Message-cache sampling pass failed for run {ProcessRunId}", _processRunId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private Task OnReadyAsync()
    {
        _initiallyReady = true;

        return Task.CompletedTask;
    }

    private static ChannelMeasurement CollectChannel(
        ulong guildId,
        ISocketMessageChannel channel,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SocketMessage> messages = channel.CachedMessages;
        DateTimeOffset? oldestMessageCreatedAtUtc = null;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (oldestMessageCreatedAtUtc is null || message.Timestamp < oldestMessageCreatedAtUtc)
                oldestMessageCreatedAtUtc = message.Timestamp.ToUniversalTime();
        }

        return new ChannelMeasurement(
            guildId.ToString(CultureInfo.InvariantCulture),
            channel.Id.ToString(CultureInfo.InvariantCulture),
            messages.Count,
            oldestMessageCreatedAtUtc);
    }

    private void Sample(CancellationToken cancellationToken)
    {
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

            IEnumerable<ISocketMessageChannel> channels = guild.Channels
                .Concat(guild.ThreadChannels)
                .DistinctBy(channel => channel.Id)
                .OfType<ISocketMessageChannel>();

            foreach (var channel in channels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (client.ConnectionState != ConnectionState.Connected || !((IGuild)guild).Available)
                    return;

                measurements.Enqueue(CollectChannel(guild.Id, channel, cancellationToken));
            }
        }

        if (measurements.Count == 0)
            return;

        LogSnapshot(measurements, cachedGuildMembers, cancellationToken);
    }

    private void LogSnapshot(Queue<ChannelMeasurement> channels, long cachedGuildMembers, CancellationToken cancellationToken)
    {
        var populatedChannels = channels.Count(channel => channel.CachedMessageCount > 0);
        var fullChannels = channels.Count(channel => _capacity > 0 && channel.CachedMessageCount >= _capacity);
        var totalCachedMessages = channels.Sum(channel => (long)channel.CachedMessageCount);

        using var process = Process.GetCurrentProcess();
        var workingSetBytes = process.WorkingSet64;
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var collectedAtUtc = DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture);

        cancellationToken.ThrowIfCancellationRequested();

        if (client.ConnectionState != ConnectionState.Connected)
            return;

        var snapshotLogger = _logger
            .ForContext("ProcessRunId", _processRunId)
            .ForContext("CollectedAtUtc", collectedAtUtc)
            .ForContext("Capacity", _capacity)
            .ForContext("SampledChannelCount", channels.Count)
            .ForContext("PopulatedChannelCount", populatedChannels)
            .ForContext("FullChannelCount", fullChannels)
            .ForContext("TotalCachedMessages", totalCachedMessages)
            .ForContext("CachedGuildMemberCount", cachedGuildMembers)
            .ForContext("ProcessWorkingSetBytes", workingSetBytes)
            .ForContext("ProcessUptime", uptime);

        snapshotLogger
            .ForContext("MessageCacheSample", value: true)
            .Debug("Message-cache snapshot: {@Channels}", channels);

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