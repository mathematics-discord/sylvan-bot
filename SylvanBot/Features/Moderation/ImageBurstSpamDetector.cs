using System.Runtime.InteropServices;

namespace SylvanBot.Features.Moderation;

public sealed class ImageBurstSpamDetector(TimeProvider timeProvider)
{
    private readonly Dictionary<ImageBurstBufferKey, Queue<ImageBurstMessage>> _buffers =
        new Dictionary<ImageBurstBufferKey, Queue<ImageBurstMessage>>();

    private readonly Lock _gate = new Lock();

    private readonly Dictionary<ImageBurstBufferKey, DateTimeOffset> _handlingLocks = [];

    public DotNext.Optional<ImageBurstDetection> Observe(ImageBurstMessage message, DotNext.Optional<ImageBurstSpamDetectorOptions> settings)
    {
        var key = new ImageBurstBufferKey(GuildId: message.GuildId, UserId: message.UserId);
        var now = timeProvider.GetUtcNow();

        lock (_gate)
        {
            PruneExpiredLocks(now);

            var isLocked = _handlingLocks.TryGetValue(key: key, out var lockedUntil) && lockedUntil > now;

            if (isLocked)
                return default;

            if (settings is not { HasValue: true, Value: var options })
                return default;

            Queue<ImageBurstMessage> buffer = GetBuffer(key);

            buffer.Enqueue(message);
            PruneOldMessages(buffer: buffer, message.Timestamp - TimeSpan.FromSeconds(options.Window));

            var channelIds = buffer
               .Select(x => x.ChannelId)
               .Distinct()
               .Order()
               .ToArray();

            if (channelIds.Length < options.DistinctChannelThreshold)
                return default;

            _handlingLocks[key] = now + options.HandlingLockDuration;

            return new DotNext.Optional<ImageBurstDetection>(
                new ImageBurstDetection(
                    GuildId: message.GuildId,
                    UserId: message.UserId,
                    buffer
                       .ToArray(),
                    ChannelIds: channelIds));
        }
    }

    private static void PruneOldMessages(Queue<ImageBurstMessage> buffer, DateTimeOffset cutoff)
    {
        while (buffer.Count > 0 && buffer.Peek().Timestamp < cutoff)
            buffer.Dequeue();
    }

    private Queue<ImageBurstMessage> GetBuffer(ImageBurstBufferKey key)
    {
        if (_buffers.TryGetValue(key: key, out Queue<ImageBurstMessage>? buffer))
            return buffer;

        Queue<ImageBurstMessage> newBuffer = new Queue<ImageBurstMessage>();
        _buffers[key] = newBuffer;

        return newBuffer;
    }

    private void PruneExpiredLocks(DateTimeOffset now)
    {
        ImageBurstBufferKey[] expiredKeys = _handlingLocks
           .Where(x => x.Value <= now)
           .Select(x => x.Key)
           .ToArray();

        foreach (var key in expiredKeys)
            _handlingLocks.Remove(key);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ImageBurstBufferKey(ulong GuildId, ulong UserId);
}