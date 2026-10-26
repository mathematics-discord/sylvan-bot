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

                var sampleTimeUtc = DateTimeOffset.UtcNow;
                var sampleId = Guid.NewGuid().ToString("N");
                var sampleLogger = _logger
                    .ForContext("ProcessRunId", _processRunId)
                    .ForContext("SampleId", sampleId)
                    .ForContext("SampleTimeUtc", sampleTimeUtc);

                try
                {
                    Sample(sampleLogger, sampleId, sampleTimeUtc, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    sampleLogger.Error(ex, "Message-cache sampling pass failed");
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

    private int SampleChannel(
        ILogger channelLogger,
        ulong guildId,
        ISocketMessageChannel channel,
        CancellationToken cancellationToken)
    {
        var messages = channel.CachedMessages;
        var count = messages.Count;
        DateTimeOffset? oldestMessageCreatedAtUtc = null;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (oldestMessageCreatedAtUtc is null || message.Timestamp < oldestMessageCreatedAtUtc)
                oldestMessageCreatedAtUtc = message.Timestamp.ToUniversalTime();
        }

        channelLogger.Debug(
            "Message-cache occupancy: guild {GuildId}, channel {ChannelId}, count {CachedMessageCount}, capacity {Capacity}, oldest {OldestMessageCreatedAtUtc}",
            guildId.ToString(CultureInfo.InvariantCulture),
            channel.Id.ToString(CultureInfo.InvariantCulture),
            count,
            _capacity,
            oldestMessageCreatedAtUtc);

        return count;
    }

    private void Sample(ILogger sampleLogger, string sampleId, DateTimeOffset sampleTimeUtc, CancellationToken cancellationToken)
    {
        var channelLogger = sampleLogger.ForContext("MessageCacheSample", true);
        var sampledChannels = 0;
        var populatedChannels = 0;
        var fullChannels = 0;
        long totalCachedMessages = 0;
        long cachedGuildMembers = 0;

        foreach (var guild in client.Guilds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (client.ConnectionState != ConnectionState.Connected)
                return;

            if (!((IGuild)guild).Available)
                continue;

            cachedGuildMembers += guild.Users.Count;
            var channels = guild.Channels
                .Concat(guild.ThreadChannels)
                .DistinctBy(channel => channel.Id)
                .OfType<ISocketMessageChannel>();

            foreach (var channel in channels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (client.ConnectionState != ConnectionState.Connected)
                    return;

                if (!((IGuild)guild).Available)
                    break;

                var count = SampleChannel(channelLogger, guild.Id, channel, cancellationToken);

                sampledChannels++;
                totalCachedMessages += count;

                if (count > 0)
                    populatedChannels++;

                if (_capacity > 0 && count >= _capacity)
                    fullChannels++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (sampledChannels == 0 || client.ConnectionState != ConnectionState.Connected)
            return;

        LogSummary(sampleLogger, sampleId, sampleTimeUtc, sampledChannels, populatedChannels, fullChannels,
            totalCachedMessages, cachedGuildMembers);
    }

    private void LogSummary(
        ILogger sampleLogger,
        string sampleId,
        DateTimeOffset sampleTimeUtc,
        int sampledChannels,
        int populatedChannels,
        int fullChannels,
        long totalCachedMessages,
        long cachedGuildMembers)
    {
        using var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        sampleLogger.Debug(
            "Message-cache summary at {SampleTimeUtc}, run {ProcessRunId}, sample {SampleId}: {SampledChannelCount} channels, {PopulatedChannelCount} populated, {FullChannelCount} full, {TotalCachedMessages} cached messages, {CachedGuildMemberCount} cached guild members, whole-process working set {ProcessWorkingSetBytes} bytes, uptime {ProcessUptime}",
            sampleTimeUtc,
            _processRunId,
            sampleId,
            sampledChannels,
            populatedChannels,
            fullChannels,
            totalCachedMessages,
            cachedGuildMembers,
            process.WorkingSet64,
            uptime);
    }
}