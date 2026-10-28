using System.Collections.Immutable;
using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.MessageCache;

/// <summary>Serializes durable mutations and exposes only snapshots inside their original lifetime.</summary>
public sealed class MessageSnapshotCache(IMessageCachePersistence persistence, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly Dictionary<(ulong GuildId, ulong MessageId), CachedMessageSnapshot> _messages = [];
    private readonly Dictionary<(ulong GuildId, ulong MessageId), Deletion> _deletions = [];
    private ulong _botId;
    private bool _ready;

    private enum DeletionState
    {
        AwaitingPublication,
        AwaitingRemoval,
        Removed,
    }

    public bool Ready => Volatile.Read(ref _ready);

    public async Task InitializeAsync(ulong botId, ImmutableArray<ulong> guildIds, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            if (_ready)
                return;

            var now = timeProvider.GetUtcNow();
            ImmutableArray<CachedMessageSnapshot> snapshots = await persistence.LoadAsync(botId, guildIds, now, ct);

            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            // don't
            foreach (var snapshot in snapshots)
            {
                var inScope = guildIds.Contains(snapshot.GuildId) && snapshot.ExpiresAtUtc > timeProvider.GetUtcNow();

                if (inScope)
                    _messages[(snapshot.GuildId, snapshot.MessageId)] = snapshot;
            }

            _botId = botId;
            Volatile.Write(ref _ready, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReceiveAsync(MessageReceived message, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            EnsureReady();

            var key = (message.GuildId, message.MessageId);
            var expired = message.CreatedAtUtc.AddHours(24) <= timeProvider.GetUtcNow();
            var alreadyObserved = Read(key).Value is Some<CachedMessageSnapshot> || _deletions.ContainsKey(key);

            if (!message.Eligible || expired || alreadyObserved)
                return;

            var snapshot = new CachedMessageSnapshot
            {
                GuildId = message.GuildId,
                ChannelId = message.ChannelId,
                MessageId = message.MessageId,
                AuthorId = message.AuthorId,
                CreatedAtUtc = message.CreatedAtUtc,
                EditedAtUtc = message.EditedAtUtc,
                Content = message.Content,
                Attachments = message.Attachments,
            };

            await PersistAsync(snapshot, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Option<MessageEdit>> EditAsync(MessageEdited message, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            EnsureReady();

            var key = (message.GuildId, message.MessageId);
            Option<CachedMessageSnapshot> before = Read(key);
            var expired = message.CreatedAtUtc.AddHours(24) <= timeProvider.GetUtcNow();
            var deleting = _deletions.ContainsKey(key);
            var ineligible = message.Eligible.Value is Some<bool>(false);

            if (expired || deleting || ineligible)
                return new None();

            Option<CachedMessageSnapshot> candidate = before.Value is Some<CachedMessageSnapshot>(var existing)
                ? ApplyToExisting(message, existing)
                : TryCreateSnapshot(message);

            if (candidate.Value is not Some<CachedMessageSnapshot>(var after) || !await PersistAsync(after, ct))
                return new None();

            return new Some<MessageEdit>(new MessageEdit { Before = before, After = after });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImmutableArray<CachedMessageSnapshot>> BeginDeleteAsync(ulong guildId, ImmutableArray<ulong> messageIds, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            EnsureReady();
            var snapshots = ImmutableArray.CreateBuilder<CachedMessageSnapshot>();

            foreach (var messageId in messageIds)
            {
                var key = (guildId, messageId);
                Option<CachedMessageSnapshot> snapshot = Read(key);

                if (_deletions.TryGetValue(key, out var deletion) && deletion.State != DeletionState.AwaitingPublication)
                    continue;

                // Freeze even a miss, so a delayed create cannot resurrect a deleted message.
                var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(messageId >> 22) + 1420070400000).AddHours(24);

                if (expiresAt > timeProvider.GetUtcNow())
                    _deletions[key] = new Deletion { ExpiresAtUtc = expiresAt, State = DeletionState.AwaitingPublication };

                if (snapshot.Value is Some<CachedMessageSnapshot>(var available))
                    snapshots.Add(available);
            }

            return snapshots.ToImmutable();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteDeleteAsync(ImmutableArray<CachedMessageSnapshot> snapshots, CancellationToken ct)
    {
        // Publication already happened. Cancellation must not prevent the in-process acknowledgement.
        await _gate.WaitAsync(CancellationToken.None);

        try
        {
            EnsureReady();

            foreach (var snapshot in snapshots)
            {
                var key = (snapshot.GuildId, snapshot.MessageId);
                _deletions[key] = new Deletion { ExpiresAtUtc = snapshot.ExpiresAtUtc, State = DeletionState.AwaitingRemoval };
                _messages.Remove(key);
            }

            // Tombstones are published before deletion I/O: ambiguous DB failures cannot duplicate a log in this process.
            foreach (var snapshot in snapshots)
            {
                await persistence.DeleteAsync(_botId, snapshot.GuildId, snapshot.MessageId, ct);
                _deletions[(snapshot.GuildId, snapshot.MessageId)] = new Deletion
                {
                    ExpiresAtUtc = snapshot.ExpiresAtUtc,
                    State = DeletionState.Removed,
                };
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImmutableArray<CachedChannelStatistics>> GetStatisticsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            return _messages
                .Values.Where(snapshot => snapshot.ExpiresAtUtc > timeProvider.GetUtcNow())
                .GroupBy(snapshot => (snapshot.GuildId, snapshot.ChannelId))
                .Select(group => new CachedChannelStatistics
                {
                    GuildId = group.Key.GuildId,
                    ChannelId = group.Key.ChannelId,
                    Count = group.Count(),
                    OldestCreatedAtUtc = group.Min(snapshot => snapshot.CreatedAtUtc),
                })
                .ToImmutableArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PruneAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            var now = timeProvider.GetUtcNow();

            ImmutableArray<(ulong GuildId, ulong MessageId)> expired = _messages
                .Where(pair => pair.Value.ExpiresAtUtc <= now)
                .Select(pair => pair.Key)
                .ToImmutableArray();

            foreach (var key in expired)
                _messages.Remove(key);

            (ulong GuildId, ulong MessageId)[] expiredDeletions = _deletions
                .Where(pair => pair.Value.ExpiresAtUtc <= now)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in expiredDeletions)
                _deletions.Remove(key);

            // Global expiry cleanup also removes expired rows left by retired bot identities.
            await persistence.DeleteExpiredAsync(now, ct);

            ImmutableArray<(ulong GuildId, ulong MessageId)> pendingRemoval = _deletions
                .Where(pair => pair.Value.State == DeletionState.AwaitingRemoval)
                .Select(pair => pair.Key)
                .ToImmutableArray();

            foreach (var key in pendingRemoval)
            {
                await persistence.DeleteAsync(_botId, key.GuildId, key.MessageId, ct);
                _deletions[key] = _deletions[key] with { State = DeletionState.Removed };
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static Option<CachedMessageSnapshot> ApplyToExisting(MessageEdited message, CachedMessageSnapshot before)
    {
        DateTimeOffset? timestamp = before.EditedAtUtc;

        if (message.EditedAtUtc.Value is Some<DateTimeOffset?>(var supplied))
        {
            if ((supplied ?? before.CreatedAtUtc) < (before.EditedAtUtc ?? before.CreatedAtUtc))
                return new None();

            timestamp = supplied;
        }

        return new Some<CachedMessageSnapshot>(
            before with
            {
                EditedAtUtc = timestamp,
                Content = message.Content.Value is Some<string>(var content) ? content : before.Content,
                Attachments = message.Attachments.Value is Some<ImmutableArray<MessageAttachment>>(var attachments)
                    ? attachments
                    : before.Attachments,
            }
        );
    }

    private static Option<CachedMessageSnapshot> TryCreateSnapshot(MessageEdited message)
    {
        if (
            message.AuthorId.Value is not Some<ulong>(var authorId)
            || message.Eligible.Value is not Some<bool>(true)
            || message.Content.Value is not Some<string>(var content)
            || message.Attachments.Value is not Some<ImmutableArray<MessageAttachment>>(var attachments)
            || message.EditedAtUtc.Value is not Some<DateTimeOffset?>(var timestamp)
        )
            return new None();

        return new Some<CachedMessageSnapshot>(
            new CachedMessageSnapshot
            {
                GuildId = message.GuildId,
                ChannelId = message.ChannelId,
                MessageId = message.MessageId,
                AuthorId = authorId,
                CreatedAtUtc = message.CreatedAtUtc,
                EditedAtUtc = timestamp,
                Content = content,
                Attachments = attachments,
            }
        );
    }

    // Called only while the cache gate is held: failed writes never publish an in-memory success.
    private async Task<bool> PersistAsync(CachedMessageSnapshot snapshot, CancellationToken ct)
    {
        await persistence.SaveAsync(_botId, snapshot, ct);

        if (snapshot.ExpiresAtUtc <= timeProvider.GetUtcNow())
            return false;

        _messages[(snapshot.GuildId, snapshot.MessageId)] = snapshot;

        return true;
    }

    private Option<CachedMessageSnapshot> Read((ulong GuildId, ulong MessageId) key) =>
        _messages.TryGetValue(key, out var snapshot) && snapshot.ExpiresAtUtc > timeProvider.GetUtcNow()
            ? new Some<CachedMessageSnapshot>(snapshot)
            : new None();

    private void EnsureReady()
    {
        if (!_ready)
            throw new InvalidOperationException("Message snapshot cache has not hydrated.");
    }

    private sealed record Deletion
    {
        public required DateTimeOffset ExpiresAtUtc { get; init; }

        public required DeletionState State { get; init; }
    }
}
