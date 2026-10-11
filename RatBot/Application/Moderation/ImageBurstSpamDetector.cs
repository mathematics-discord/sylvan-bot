using System.Runtime.InteropServices;

namespace RatBot.Application.Moderation;

public sealed class ImageBurstSpamDetector(TimeProvider timeProvider, ImageBurstSpamDetectorSettings settings)
{
    private readonly Dictionary<ImageBurstBufferKey, Queue<ImageBurstMessage>> _buffers =
        new Dictionary<ImageBurstBufferKey, Queue<ImageBurstMessage>>();

    private readonly Lock _gate = new Lock();

    private readonly Dictionary<ImageBurstBufferKey, DateTimeOffset> _handlingLocks =
        new Dictionary<ImageBurstBufferKey, DateTimeOffset>();

    public ImageBurstSpamDetector()
        : this(TimeProvider.System, new ImageBurstSpamDetectorOptions())
    {
    }

    public ImageBurstSpamDetector(TimeProvider timeProvider, ImageBurstSpamDetectorOptions options)
        : this(timeProvider, CreateSettings(options))
    {
    }

    private static ImageBurstSpamDetectorSettings CreateSettings(ImageBurstSpamDetectorOptions options)
    {
        ImageBurstSpamDetectorSettings settings = new ImageBurstSpamDetectorSettings(options);
        settings.Update(1, options);
        return settings;
    }

    private static void PruneOldMessages(Queue<ImageBurstMessage> buffer, DateTimeOffset cutoff)
    {
        while (buffer.Count > 0 && buffer.Peek().Timestamp < cutoff)
            buffer.Dequeue();
    }

    public ImageBurstDetection? Observe(ImageBurstMessage message)
    {
        ImageBurstBufferKey key = new ImageBurstBufferKey(message.GuildId, message.UserId);
        DateTimeOffset now = timeProvider.GetUtcNow();

        lock (_gate)
        {
            PruneExpiredLocks(now);

            if (_handlingLocks.TryGetValue(key, out DateTimeOffset lockedUntil) && lockedUntil > now)
                return null;

            if (!settings.TryGet(message.GuildId, out ImageBurstSpamDetectorOptions options))
                return null;

            Queue<ImageBurstMessage> buffer = GetBuffer(key);

            buffer.Enqueue(message);
            PruneOldMessages(buffer, message.Timestamp - TimeSpan.FromSeconds(options.Window));

            ulong[] channelIds = buffer.Select(x => x.ChannelId).Distinct().Order().ToArray();

            if (channelIds.Length < options.DistinctChannelThreshold)
                return null;

            _handlingLocks[key] = now + options.HandlingLockDuration;

            return new ImageBurstDetection(message.GuildId, message.UserId, buffer.ToArray(), channelIds);
        }
    }

    private Queue<ImageBurstMessage> GetBuffer(ImageBurstBufferKey key)
    {
        if (_buffers.TryGetValue(key, out Queue<ImageBurstMessage>? buffer))
            return buffer;

        Queue<ImageBurstMessage> newBuffer = new Queue<ImageBurstMessage>();
        _buffers[key] = newBuffer;

        return newBuffer;
    }

    private void PruneExpiredLocks(DateTimeOffset now)
    {
        ImageBurstBufferKey[] expiredKeys = _handlingLocks.Where(x => x.Value <= now).Select(x => x.Key).ToArray();

        foreach (ImageBurstBufferKey key in expiredKeys)
            _handlingLocks.Remove(key);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ImageBurstBufferKey(ulong GuildId, ulong UserId);
}