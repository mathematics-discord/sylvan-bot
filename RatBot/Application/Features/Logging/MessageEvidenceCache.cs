using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace RatBot.Application.Features.Logging;

public sealed class MessageEvidenceCache(EvidenceCacheSettings settings)
{
    private readonly Dictionary<EvidenceKey, CacheEntry> _entries = new Dictionary<EvidenceKey, CacheEntry>();

    private readonly PriorityQueue<QueuedEntry, DateTimeOffset> _evictionQueue =
        new PriorityQueue<QueuedEntry, DateTimeOffset>();

    private readonly PriorityQueue<QueuedEntry, DateTimeOffset> _expiryQueue =
        new PriorityQueue<QueuedEntry, DateTimeOffset>();

    private readonly Lock _lock = new Lock();
    private long _totalAttachmentBytes;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                EvictExpired(DateTimeOffset.UtcNow);
                return _entries.Count;
            }
        }
    }

    private static MessageEvidence Empty(ulong guildId, ulong messageId)
    {
        return new MessageEvidence(guildId, 0, messageId, 0, DateTimeOffset.MinValue, null,
            ImmutableArray<CachedAttachmentEvidence>.Empty);
    }

    private static long TotalBytes(IEnumerable<CachedAttachmentEvidence> attachments)
    {
        return attachments.Sum(attachment => attachment.Bytes.LongLength);
    }

    public IReadOnlyDictionary<ulong, MessageEvidence> GetMany(ulong guildId, IEnumerable<ulong> messageIds,
        DateTimeOffset nowUtc)
    {
        Dictionary<ulong, MessageEvidence> results = new Dictionary<ulong, MessageEvidence>();

        lock (_lock)
        {
            EvictExpired(nowUtc);

            foreach (ulong messageId in messageIds)
            {
                EvidenceKey key = new EvidenceKey(guildId, messageId);

                if (_entries.TryGetValue(key, out CacheEntry? entry))
                    results[messageId] = entry.Evidence;
            }
        }

        return results;
    }

    public void Put(MessageEvidence evidence, DateTimeOffset nowUtc, TimeSpan retentionPeriod)
    {
        settings.Validate();

        if (retentionPeriod <= TimeSpan.Zero)
            throw new InvalidOperationException("Evidence retention period must be positive.");

        ImmutableArray<CachedAttachmentEvidence> attachments = evidence
            .Attachments.Where(attachment => attachment.Bytes.LongLength <= settings.MaxBytesPerAttachment)
            .Take(settings.MaxAttachmentCountPerMessage)
            .Select(attachment => attachment with { Bytes = attachment.Bytes.ToArray() })
            .ToImmutableArray();

        MessageEvidence cachedEvidence = evidence with { Attachments = attachments };
        CacheEntry entry = new CacheEntry(cachedEvidence, nowUtc, nowUtc + retentionPeriod, TotalBytes(attachments));

        lock (_lock)
        {
            EvictExpired(nowUtc);

            EvidenceKey key = new EvidenceKey(evidence.GuildId, evidence.MessageId);

            if (_entries.Remove(key, out CacheEntry? previous))
                _totalAttachmentBytes -= previous.AttachmentBytes;

            _entries[key] = entry;
            _totalAttachmentBytes += entry.AttachmentBytes;
            Enqueue(key, entry);

            EvictOverflow();
        }
    }

    public void Remove(ulong guildId, ulong messageId)
    {
        lock (_lock)
        {
            if (_entries.Remove(new EvidenceKey(guildId, messageId), out CacheEntry? entry))
                _totalAttachmentBytes -= entry.AttachmentBytes;
        }
    }

    public bool TryGet(ulong guildId, ulong messageId, DateTimeOffset nowUtc, out MessageEvidence evidence)
    {
        lock (_lock)
        {
            EvictExpired(nowUtc);

            if (!_entries.TryGetValue(new EvidenceKey(guildId, messageId), out CacheEntry? entry))
            {
                evidence = Empty(guildId, messageId);
                return false;
            }

            evidence = entry.Evidence;
            return true;
        }
    }

    private void Enqueue(EvidenceKey key, CacheEntry entry)
    {
        QueuedEntry queued = new QueuedEntry(key, entry.LastTouchedAtUtc, entry.ExpiresAtUtc);
        _evictionQueue.Enqueue(queued, entry.LastTouchedAtUtc);
        _expiryQueue.Enqueue(queued, entry.ExpiresAtUtc);
    }

    private void EvictExpired(DateTimeOffset nowUtc)
    {
        while (_expiryQueue.TryPeek(out QueuedEntry? queued, out DateTimeOffset expiresAtUtc) && queued is not null &&
               expiresAtUtc <= nowUtc)
        {
            _expiryQueue.Dequeue();

            if (!IsCurrent(queued, out CacheEntry? entry))
                continue;

            _entries.Remove(queued.Key);
            _totalAttachmentBytes -= entry.AttachmentBytes;
        }
    }

    private void EvictOverflow()
    {
        while (_entries.Count > settings.MaxCachedMessageCount ||
               _totalAttachmentBytes > settings.MaxTotalCachedAttachmentBytes)
        {
            if (!_evictionQueue.TryDequeue(out QueuedEntry? queued, out DateTimeOffset _) || queued is null)
                return;

            if (!IsCurrent(queued, out CacheEntry? entry))
                continue;

            _entries.Remove(queued.Key);
            _totalAttachmentBytes -= entry.AttachmentBytes;
        }
    }

    private bool IsCurrent(QueuedEntry queued, [NotNullWhen(true)] out CacheEntry? entry)
    {
        if (!_entries.TryGetValue(queued.Key, out entry))
            return false;

        return entry.LastTouchedAtUtc == queued.LastTouchedAtUtc && entry.ExpiresAtUtc == queued.ExpiresAtUtc;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct EvidenceKey(ulong GuildId, ulong MessageId);

    private sealed record CacheEntry(
        MessageEvidence Evidence,
        DateTimeOffset LastTouchedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        long AttachmentBytes);

    private sealed record QueuedEntry(EvidenceKey Key, DateTimeOffset LastTouchedAtUtc, DateTimeOffset ExpiresAtUtc);
}