using System.Runtime.InteropServices;

namespace RatBot.Features.Moderation;

public sealed class ImageBurstSpamDetector(TimeProvider timeProvider, ImageBurstSpamDetectorSettings settings)
{
    private readonly Dictionary<ImageBurstBufferKey, Queue<ImageBurstMessage>> _buffers = new();

    private readonly Lock _gate = new();

    private readonly Dictionary<ImageBurstBufferKey, DateTimeOffset> _handlingLocks = new();

    public ImageBurstSpamDetector() : this(TimeProvider.System, new ImageBurstSpamDetectorOptions())
    {
    }

    public ImageBurstSpamDetector(TimeProvider timeProvider, ImageBurstSpamDetectorOptions options)
        : this(timeProvider, CreateSettings(options))
    {
    }

    private static ImageBurstSpamDetectorSettings CreateSettings(ImageBurstSpamDetectorOptions options)
    {
        var settings = new ImageBurstSpamDetectorSettings(options);
        settings.Update(1, options);
        return settings;
    }

    private static void PruneOldMessages(Queue<ImageBurstMessage> buffer, DateTimeOffset cutoff)
    {
        while (buffer.Count > 0 && buffer.Peek().Timestamp < cutoff)
            buffer.Dequeue();
    }

    public Option<ImageBurstDetection> Observe(ImageBurstMessage message)
    {
        var key = new ImageBurstBufferKey(message.GuildId, message.UserId);
        var now = timeProvider.GetUtcNow();

        lock (_gate)
        {
            PruneExpiredLocks(now);

            var isLocked = _handlingLocks.TryGetValue(key, out var lockedUntil) && lockedUntil > now;
            var optionsUnavailable = !settings.TryGet(message.GuildId, out var options);

            if (isLocked || optionsUnavailable)
                return new None();

            Queue<ImageBurstMessage> buffer = GetBuffer(key);

            buffer.Enqueue(message);
            PruneOldMessages(buffer, message.Timestamp - TimeSpan.FromSeconds(options.Window));

            var channelIds = buffer.Select(x => x.ChannelId).Distinct().Order().ToArray();

            if (channelIds.Length < options.DistinctChannelThreshold)
                return new None();

            _handlingLocks[key] = now + options.HandlingLockDuration;

            return new Some<ImageBurstDetection>(
                new ImageBurstDetection(
                    GuildId: message.GuildId,
                    UserId: message.UserId,
                    Messages: buffer.ToArray(),
                    ChannelIds: channelIds));
        }
    }

    private Queue<ImageBurstMessage> GetBuffer(ImageBurstBufferKey key)
    {
        if (_buffers.TryGetValue(key, out Queue<ImageBurstMessage>? buffer))
            return buffer;

        Queue<ImageBurstMessage> newBuffer = new();
        _buffers[key] = newBuffer;

        return newBuffer;
    }

    private void PruneExpiredLocks(DateTimeOffset now)
    {
        ImageBurstBufferKey[] expiredKeys = _handlingLocks.Where(x => x.Value <= now).Select(x => x.Key).ToArray();

        foreach (var key in expiredKeys)
            _handlingLocks.Remove(key);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ImageBurstBufferKey(ulong GuildId, ulong UserId);
}