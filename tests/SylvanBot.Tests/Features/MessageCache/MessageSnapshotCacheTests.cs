using System.Collections.Immutable;
using SylvanBot.Application.Features.MessageCache;

namespace SylvanBot.Tests.Features.MessageCache;

[TestFixture]
public sealed class MessageSnapshotCacheTests
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(
        year: 2026,
        month: 10,
        day: 28,
        hour: 0,
        minute: 0,
        second: 0,
        offset: TimeSpan.Zero);

    private static ulong Id => (ulong)(Created.ToUnixTimeMilliseconds() - 1420070400000) << 22;

    [Test]
    public async Task Edit_Restart_DoesNotExtendOriginalExpiry()
    {
        // Arrange
        var clock = new CacheClock(Created.AddHours(23));
        var persistence = new MemoryPersistence();
        using var cache = new MessageSnapshotCache(persistence: persistence, timeProvider: clock);
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("original"), ct: CancellationToken.None);

        // Act
        await cache.EditAsync(Edit("edited") with { EditedAtUtc = new Optional<DateTimeOffset?>(clock.GetUtcNow()) }, ct: CancellationToken.None);
        using var restarted = new MessageSnapshotCache(persistence: persistence, timeProvider: clock);
        await restarted.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        ImmutableArray<CachedMessageSnapshot> restored = await restarted.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        clock.Now = Created.AddHours(24);
        ImmutableArray<CachedMessageSnapshot> expired = await restarted.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        Optional<MessageEdit> oldEdit = await restarted.EditAsync(Edit("too late"), ct: CancellationToken.None);

        // Assert
        restored.Single().ExpiresAtUtc.ShouldBe(Created.AddHours(24));
        restored.Single().Content.ShouldBe("edited");
        expired.ShouldBeEmpty();
        oldEdit.IsUndefined.ShouldBeTrue();
        (await restarted.GetStatisticsAsync(CancellationToken.None)).ShouldBeEmpty();
        await restarted.PruneAsync(CancellationToken.None);
        persistence.Rows.ShouldBeEmpty();
    }

    [Test]
    public async Task Edit_PartialUpdate_PreservesOmittedAndAppliesClearedFields()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        using var cache = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("original"), ct: CancellationToken.None);

        // Act
        Optional<MessageEdit> partial = await cache.EditAsync(
            Edit("unused") with
            {
                AuthorId = default,
                Eligible = default,
                Content = default,
            },
            ct: CancellationToken.None
        );

        Optional<MessageEdit> cleared = await cache.EditAsync(
            Edit(string.Empty) with
            {
                Attachments = new Optional<ImmutableArray<MessageAttachment>>([]),
                EditedAtUtc = new Optional<DateTimeOffset?>(Created.AddMinutes(5)),
            },
            ct: CancellationToken.None
        );

        // Assert
        partial
           .Value.Before.Value.Content.ShouldBe("original");
        partial.Value.After.ShouldNotBeNull().Content.ShouldBe("original");
        partial.Value.After.Attachments.Length.ShouldBe(1);

        cleared
           .Value.Before.Value.Attachments.Length.ShouldBe(1);
        cleared.Value.After.ShouldNotBeNull().Content.ShouldBeEmpty();
        cleared.Value.After.Attachments.ShouldBeEmpty();
    }

    [Test]
    public async Task Edit_SuccessiveAndStaleEvents_UsesLatestPriorSnapshot()
    {
        // Arrange
        using var cache = new MessageSnapshotCache(new MemoryPersistence(), new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("one"), ct: CancellationToken.None);

        // Act
        Optional<MessageEdit> first = await cache.EditAsync(Edit("two"), ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("one"), ct: CancellationToken.None);

        Optional<MessageEdit> second = await cache.EditAsync(
            Edit("three") with
            {
                EditedAtUtc = new Optional<DateTimeOffset?>(Created.AddMinutes(2)),
            },
            ct: CancellationToken.None
        );
        await cache.EditAsync(Edit("stale") with { EditedAtUtc = new Optional<DateTimeOffset?>(Created.AddMinutes(1)) }, ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("one"), ct: CancellationToken.None);

        // Assert
        first
           .Value.Before.Value.Content.ShouldBe("one");

        second
           .Value.Before.Value.Content.ShouldBe("two");
        (await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None)).Single().Content.ShouldBe("three");
    }

    [Test]
    public async Task Edit_TimestampPresence_PreservesOmissionAndRejectsStaleNull()
    {
        // Arrange
        using var cache = new MessageSnapshotCache(new MemoryPersistence(), new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("original"), ct: CancellationToken.None);
        var timestamp = Created.AddMinutes(5);

        // Act
        Optional<MessageEdit> unedited = await cache.EditAsync(
            new MessageEdited
            {
                GuildId = 10,
                ChannelId = 20,
                MessageId = Id,
                CreatedAtUtc = Created,
                EditedAtUtc = new Optional<DateTimeOffset?>(null),
            },
            ct: CancellationToken.None
        );

        await cache.EditAsync(Edit("first edit") with { EditedAtUtc = new Optional<DateTimeOffset?>(timestamp) }, ct: CancellationToken.None);
        Optional<MessageEdit> omitted = await cache.EditAsync(Edit("second edit"), ct: CancellationToken.None);

        Optional<MessageEdit> staleNull = await cache.EditAsync(
            Edit("stale") with
            {
                EditedAtUtc = new Optional<DateTimeOffset?>(null),
            },
            ct: CancellationToken.None
        );

        // Assert
        unedited.Value.After.ShouldNotBeNull().EditedAtUtc.ShouldBeNull();
        unedited.Value.After.Content.ShouldBe("original");
        omitted.Value.After.ShouldNotBeNull().EditedAtUtc.ShouldBe(timestamp);

        omitted
           .Value.Before.Value.Content.ShouldBe("first edit");
        staleNull.IsUndefined.ShouldBeTrue();
        (await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None)).Single().Content.ShouldBe("second edit");
    }

    [Test]
    public async Task Edit_OmittedIdentity_DoesNotSeedAnUnknownMessage()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        using var cache = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);

        var partial = new MessageEdited
        {
            GuildId = 10,
            ChannelId = 20,
            MessageId = Id,
            CreatedAtUtc = Created,
            Content = new Optional<string>("known current content"),
        };

        // Act
        Optional<MessageEdit> result = await cache.EditAsync(message: partial, ct: CancellationToken.None);

        // Assert
        result.IsUndefined.ShouldBeTrue();
        persistence.Rows.ShouldBeEmpty();
    }

    [Test]
    public async Task Receive_WriteInFlight_DeletionWaitsForDurability()
    {
        // Arrange
        var persistence = new MemoryPersistence { SaveRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cache = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);

        // Act
        var create = cache.ReceiveAsync(Received("evidence"), ct: CancellationToken.None);
        await persistence.SaveStarted.Task;
        Task<ImmutableArray<CachedMessageSnapshot>> delete = cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        var waiting = !delete.IsCompleted;
        persistence.SaveRelease.SetResult();
        await create;
        ImmutableArray<CachedMessageSnapshot> deleted = await delete;

        // Assert
        waiting.ShouldBeTrue();
        deleted.Single().Content.ShouldBe("evidence");
        persistence.Rows.Count.ShouldBe(1);
    }

    [Test]
    public async Task Edit_FailedOrCancelledWrite_PreservesPriorSnapshot()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        using var cache = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("durable"), ct: CancellationToken.None);
        persistence.FailSave = true;

        // Act
        await Should.ThrowAsync<InvalidOperationException>(() => cache.EditAsync(Edit("failed"), ct: CancellationToken.None));
        persistence.FailSave = false;
        persistence.SaveRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        Task<Optional<MessageEdit>> write = cache.EditAsync(Edit("cancelled"), ct: cancellation.Token);
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => write);

        // Assert
        persistence.Rows.Single().Value.Content.ShouldBe("durable");
    }

    [Test]
    public async Task Delete_FailedPublicationAndDatabaseAcknowledgement_RetainsThenSuppressesEvidence()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        var clock = new CacheClock(Created.AddHours(1));
        using var cache = new MessageSnapshotCache(persistence: persistence, timeProvider: clock);
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("evidence"), ct: CancellationToken.None);

        // Act
        await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        // No acknowledgement: publication failed. A later event can still use the frozen snapshot.
        await cache.EditAsync(Edit("delayed edit"), ct: CancellationToken.None);
        ImmutableArray<CachedMessageSnapshot> retry = await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        persistence.FailDelete = true;
        await Should.ThrowAsync<InvalidOperationException>(() => cache.CompleteDeleteAsync(snapshots: retry, ct: CancellationToken.None));
        await cache.ReceiveAsync(Received("delayed create"), ct: CancellationToken.None);
        ImmutableArray<CachedMessageSnapshot> duplicate = await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        persistence.FailDelete = false;
        await cache.PruneAsync(CancellationToken.None);

        // Assert
        retry.Single().Content.ShouldBe("evidence");
        duplicate.ShouldBeEmpty();
        (await cache.GetStatisticsAsync(CancellationToken.None)).ShouldBeEmpty();
        persistence.Rows.ShouldBeEmpty();
    }

    [Test]
    public async Task Initialize_HydrationInFlight_NewObservationWaitsAndUsesHydratedBefore()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        using var original = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(1)));
        await original.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await original.ReceiveAsync(Received("before restart"), ct: CancellationToken.None);
        persistence.LoadRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var restarted = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(2)));

        // Act
        var initialize = restarted.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);

        Task<Optional<MessageEdit>> edit = restarted.EditAsync(
            Edit("after restart") with
            {
                EditedAtUtc = new Optional<DateTimeOffset?>(Created.AddHours(2)),
            },
            ct: CancellationToken.None
        );
        var waiting = !edit.IsCompleted && !restarted.Ready;
        persistence.LoadRelease.SetResult();
        await initialize;
        Optional<MessageEdit> result = await edit;

        // Assert
        waiting.ShouldBeTrue();

        result
           .Value.Before.Value.Content.ShouldBe("before restart");
        result.Value.After.ShouldNotBeNull().Content.ShouldBe("after restart");
    }

    [Test]
    public async Task Initialize_FailureAndDifferentOwnership_DoesNotExposeOtherSnapshots()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        var clock = new CacheClock(Created.AddHours(1));
        using var original = new MessageSnapshotCache(persistence: persistence, timeProvider: clock);
        await original.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await original.ReceiveAsync(Received("other bot"), ct: CancellationToken.None);
        using var restarted = new MessageSnapshotCache(persistence: persistence, timeProvider: clock);
        persistence.FailLoad = true;

        // Act
        await Should.ThrowAsync<InvalidOperationException>(() => restarted.InitializeAsync(botId: 2, [10], ct: CancellationToken.None));
        var failedReadiness = restarted.Ready;
        await Should.ThrowAsync<InvalidOperationException>(() => restarted.ReceiveAsync(Received("not ready"), ct: CancellationToken.None));
        persistence.FailLoad = false;
        await restarted.InitializeAsync(botId: 2, [20], ct: CancellationToken.None);

        Optional<MessageEdit> seeded = await restarted.EditAsync(
            Edit("current") with
            {
                GuildId = 20,
                Attachments = new Optional<ImmutableArray<MessageAttachment>>([]),
                EditedAtUtc = new Optional<DateTimeOffset?>(null),
            },
            ct: CancellationToken.None
        );

        // Assert
        failedReadiness.ShouldBeFalse();
        seeded.Value.Before.IsUndefined.ShouldBeTrue();
        (await restarted.GetStatisticsAsync(CancellationToken.None)).Single().Count.ShouldBe(1);
    }

    [Test]
    public async Task Edit_SeedRequiresCompleteFields_EmptyAttachmentsAreExplicit()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        using var cache = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        var partial = Edit("known content") with { EditedAtUtc = new Optional<DateTimeOffset?>(null) };

        // Act
        Optional<MessageEdit> missingAttachments = await cache.EditAsync(message: partial, ct: CancellationToken.None);

        Optional<MessageEdit> missingTimestamp = await cache.EditAsync(
            Edit("known content") with
            {
                Attachments = new Optional<ImmutableArray<MessageAttachment>>([]),
            },
            ct: CancellationToken.None
        );

        Optional<MessageEdit> complete = await cache.EditAsync(
            partial with
            {
                Attachments = new Optional<ImmutableArray<MessageAttachment>>([]),
            },
            ct: CancellationToken.None
        );

        // Assert
        missingAttachments.IsUndefined.ShouldBeTrue();
        missingTimestamp.IsUndefined.ShouldBeTrue();
        var change = complete.Value;
        change.Before.IsUndefined.ShouldBeTrue();
        change.After.Attachments.ShouldBeEmpty();
        persistence.Rows.Count.ShouldBe(1);
    }

    [Test]
    public async Task Statistics_ExpiryWithoutCleanup_ExcludesExpiredEntries()
    {
        // Arrange
        var clock = new CacheClock(Created.AddHours(1));
        using var cache = new MessageSnapshotCache(new MemoryPersistence(), timeProvider: clock);
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("evidence"), ct: CancellationToken.None);

        // Act
        ImmutableArray<CachedChannelStatistics> live = await cache.GetStatisticsAsync(CancellationToken.None);
        clock.Now = Created.AddHours(24);
        ImmutableArray<CachedChannelStatistics> expired = await cache.GetStatisticsAsync(CancellationToken.None);

        // Assert
        live.Single().Count.ShouldBe(1);
        live.Single().OldestCreatedAtUtc.ShouldBe(Created);
        live.Single().OldestExpiresAtUtc.ShouldBe(Created.AddHours(24));
        expired.ShouldBeEmpty();
    }

    [Test]
    public async Task Edit_ExpiryDuringWrite_ReturnsNoUsableChange()
    {
        // Arrange
        var clock = new CacheClock(Created.AddHours(23));
        var persistence = new MemoryPersistence();
        using var cache = new MessageSnapshotCache(persistence: persistence, timeProvider: clock);
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("original"), ct: CancellationToken.None);
        persistence.SaveRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        Task<Optional<MessageEdit>> edit = cache.EditAsync(Edit("current"), ct: CancellationToken.None);
        clock.Now = Created.AddHours(24);
        persistence.SaveRelease.SetResult();
        Optional<MessageEdit> result = await edit;

        // Assert
        result.IsUndefined.ShouldBeTrue();
        (await cache.GetStatisticsAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task Receive_ExpiryDuringWrite_DoesNotPublishExpiredSnapshot()
    {
        // Arrange
        var clock = new CacheClock(Created.AddHours(23));
        var persistence = new MemoryPersistence { SaveRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cache = new MessageSnapshotCache(persistence: persistence, timeProvider: clock);
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);

        // Act
        var write = cache.ReceiveAsync(Received("expires while saving"), ct: CancellationToken.None);
        await persistence.SaveStarted.Task;
        clock.Now = Created.AddHours(24);
        persistence.SaveRelease.SetResult();
        await write;

        // Assert
        (await cache.GetStatisticsAsync(CancellationToken.None)).ShouldBeEmpty();
        await cache.PruneAsync(CancellationToken.None);
        persistence.Rows.ShouldBeEmpty();
    }

    [Test]
    public async Task Delete_Miss_PreventsDelayedCreateFromResurrectingMessage()
    {
        // Arrange
        using var cache = new MessageSnapshotCache(new MemoryPersistence(), new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);

        // Act
        await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("late create"), ct: CancellationToken.None);

        // Assert
        (await cache.GetStatisticsAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task CompleteDelete_CancellationAfterPublication_StillAcknowledgesInMemory()
    {
        // Arrange
        var persistence = new MemoryPersistence();
        using var cache = new MessageSnapshotCache(persistence: persistence, new CacheClock(Created.AddHours(1)));
        await cache.InitializeAsync(botId: 1, [10], ct: CancellationToken.None);
        await cache.ReceiveAsync(Received("published"), ct: CancellationToken.None);
        ImmutableArray<CachedMessageSnapshot> deletion = await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);

        // Act
        await Should.ThrowAsync<OperationCanceledException>(() => cache.CompleteDeleteAsync(
                                                                snapshots: deletion,
                                                                new CancellationToken(canceled: true)));
        ImmutableArray<CachedMessageSnapshot> duplicate = await cache.BeginDeleteAsync(guildId: 10, [Id], ct: CancellationToken.None);
        await cache.PruneAsync(CancellationToken.None);

        // Assert
        duplicate.ShouldBeEmpty();
        persistence.Rows.ShouldBeEmpty();
    }

    private static MessageReceived Received(string content) =>
        new MessageReceived
        {
            GuildId = 10,
            ChannelId = 20,
            MessageId = Id,
            AuthorId = 30,
            CreatedAtUtc = Created,
            Eligible = true,
            Content = content,
            EditedAtUtc = null,
            Attachments = [new MessageAttachment("https://example.invalid/image", ProxyUrl: null, "image/png", Size: 100)],
        };

    private static MessageEdited Edit(string content) =>
        new MessageEdited
        {
            GuildId = 10,
            ChannelId = 20,
            MessageId = Id,
            CreatedAtUtc = Created,
            AuthorId = new Optional<ulong>(30),
            Eligible = new Optional<bool>(true),
            Content = new Optional<string>(content),
        };

    private sealed class CacheClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryPersistence : IMessageCachePersistence
    {
        public Dictionary<(ulong BotId, ulong GuildId, ulong MessageId), CachedMessageSnapshot> Rows { get; } = [];

        public bool FailSave { get; set; }

        public bool FailLoad { get; set; }

        public bool FailDelete { get; set; }

        public TaskCompletionSource? SaveRelease { get; set; }

        public TaskCompletionSource? LoadRelease { get; set; }

        public TaskCompletionSource SaveStarted { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ImmutableArray<CachedMessageSnapshot>> LoadAsync(
            ulong botId,
            ImmutableArray<ulong> guildIds,
            DateTimeOffset now,
            CancellationToken ct
        )
        {
            if (FailLoad)
                throw new InvalidOperationException("Unavailable");

            if (LoadRelease is not null)
                await LoadRelease.Task.WaitAsync(ct);

            return Rows.Where(pair => pair.Key.BotId == botId && guildIds.Contains(pair.Key.GuildId) && pair.Value.ExpiresAtUtc > now)
               .Select(pair => pair.Value)
               .ToImmutableArray();
        }

        public async Task SaveAsync(ulong botId, CachedMessageSnapshot snapshot, CancellationToken ct)
        {
            SaveStarted.TrySetResult();

            if (SaveRelease is not null)
                await SaveRelease.Task.WaitAsync(ct);

            if (FailSave)
                throw new InvalidOperationException("Unavailable");

            Rows[(botId, snapshot.GuildId, snapshot.MessageId)] = snapshot;
        }

        public Task DeleteAsync(ulong botId, ulong guildId, ulong messageId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (FailDelete)
                throw new InvalidOperationException("Unavailable");

            Rows.Remove((botId, guildId, messageId));

            return Task.CompletedTask;
        }

        public Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct)
        {
            ImmutableArray<(ulong BotId, ulong GuildId, ulong MessageId)> expired = Rows.Where(pair => pair.Value.ExpiresAtUtc <= now)
               .Select(pair => pair.Key)
               .ToImmutableArray();

            foreach (var key in expired)
                Rows.Remove(key);

            return Task.CompletedTask;
        }
    }
}