namespace SylvanBot.Features.Moderation;

using System.Collections.Concurrent;
using SylvanBot.Application.Common;

public sealed class ImageBurstSpamDetector(
    TimeProvider timeProvider,
    ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild)
{
    private readonly Dictionary<ImageBurstBufferKey, Queue<ImageBurstMessage>> _buffers = new();

    private readonly Lock _gate = new();

    private readonly Dictionary<ImageBurstBufferKey, DateTimeOffset> _handlingLocks = [];

    public Option<ImageBurstDetection> Observe(ImageBurstMessage message)
    {
        var key = new ImageBurstBufferKey(message.GuildId, message.UserId);
        var now = timeProvider.GetUtcNow();

        lock (_gate)
        {
            PruneExpiredLocks(now);

            var isLocked = _handlingLocks.TryGetValue(key, out var lockedUntil) && lockedUntil > now;
            if (isLocked)
                return new None();

            if (!settingsByGuild.TryGetValue(message.GuildId, out var options))
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

    private static void PruneOldMessages(Queue<ImageBurstMessage> buffer, DateTimeOffset cutoff)
    {
        while (buffer.Count > 0 && buffer.Peek().Timestamp < cutoff)
            buffer.Dequeue();
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

    private readonly record struct ImageBurstBufferKey(ulong GuildId, ulong UserId);
}