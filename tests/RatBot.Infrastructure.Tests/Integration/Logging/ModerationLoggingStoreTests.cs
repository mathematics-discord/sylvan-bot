using Microsoft.EntityFrameworkCore;
using RatBot.Domain.Features.Logging;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Features.Logging;

namespace RatBot.Infrastructure.Tests.Integration.Logging;

[TestFixture]
public sealed class ModerationLoggingStoreTests
{
    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ExcludeAsync_AndIncludeAsync_TogglePersistedExclusion()
    {
        ModerationLoggingStore store = CreateStore();

        ExcludeChannelResult firstExclude =
            await store.ExcludeAsync(1, 2, DateTimeOffset.UtcNow, CancellationToken.None);
        ExcludeChannelResult secondExclude =
            await store.ExcludeAsync(1, 2, DateTimeOffset.UtcNow, CancellationToken.None);
        IReadOnlyList<LoggingExcludedChannel> exclusions = await store.ListExclusionsAsync(1, CancellationToken.None);
        IncludeChannelResult firstInclude = await store.IncludeAsync(1, 2, CancellationToken.None);
        IncludeChannelResult secondInclude = await store.IncludeAsync(1, 2, CancellationToken.None);

        firstExclude.ShouldBe(ExcludeChannelResult.Excluded);
        secondExclude.ShouldBe(ExcludeChannelResult.AlreadyExcluded);
        exclusions.Single().ChannelId.ShouldBe(2UL);
        firstInclude.ShouldBe(IncludeChannelResult.Included);
        secondInclude.ShouldBe(IncludeChannelResult.NotExcluded);
    }

    [Test]
    public async Task UpdateConfigurationAsync_PartialUpdatePreservesOmittedValues()
    {
        ModerationLoggingStore store = CreateStore();

        ErrorOr<LoggingConfiguration> first = await store
            .UpdateConfigurationAsync(1, true, 10, null, TimeSpan.FromMinutes(30), CancellationToken.None)
            .ConfigureAwait(false);
        ErrorOr<LoggingConfiguration> second = await store
            .UpdateConfigurationAsync(1, null, null, 20, null, CancellationToken.None)
            .ConfigureAwait(false);

        first.IsError.ShouldBeFalse();
        second.IsError.ShouldBeFalse();
        second.Value.Enabled.ShouldBeTrue();
        second.Value.DeleteLogChannelId.ShouldBe(10UL);
        second.Value.EditLogChannelId.ShouldBe(20UL);
        second.Value.EvidenceRetentionPeriod.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Test]
    public async Task UpdateConfigurationAsync_WhenEnablingWithoutEitherChannel_ReturnsError()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        ModerationLoggingStore store = CreateStore();

        ErrorOr<LoggingConfiguration> result = await store
            .UpdateConfigurationAsync(1, true, null, null, null, CancellationToken.None)
            .ConfigureAwait(false);

        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldBe("Enable logging only after setting a delete or edit log channel.");
        (await db.LoggingConfigurations.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task GetConfigurationAsync_ReturnsCurrentPersistedStatePerGuild()
    {
        ModerationLoggingStore store = CreateStore();
        await store.UpdateConfigurationAsync(1, true, 10, null, null, CancellationToken.None).ConfigureAwait(false);
        await store.UpdateConfigurationAsync(2, false, 20, null, null, CancellationToken.None).ConfigureAwait(false);

        LoggingConfiguration firstGuildBefore =
            await store.GetConfigurationAsync(1, CancellationToken.None).ConfigureAwait(false);
        LoggingConfiguration secondGuild =
            await store.GetConfigurationAsync(2, CancellationToken.None).ConfigureAwait(false);
        await store.UpdateConfigurationAsync(1, false, null, null, null, CancellationToken.None).ConfigureAwait(false);
        LoggingConfiguration firstGuildAfter =
            await store.GetConfigurationAsync(1, CancellationToken.None).ConfigureAwait(false);

        firstGuildBefore.AllowsLogging(false).ShouldBeTrue();
        secondGuild.AllowsLogging(false).ShouldBeFalse();
        firstGuildAfter.AllowsLogging(false).ShouldBeFalse();
    }

    [Test]
    public async Task ExcludedChannelGate_PreventsObservationAndLogEntryPersistence()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        ModerationLoggingStore store = CreateStore();
        await store.ExcludeAsync(1, 2, DateTimeOffset.UtcNow, CancellationToken.None);

        if (!await store.IsExcludedAsync(1, 2, CancellationToken.None))
        {
            await store.ObserveMessageAsync(new ObservedMessage(10, 1, 2, 3, DateTimeOffset.UtcNow),
                CancellationToken.None);
            await store.RecordLogEntriesAsync(new[] { new MessageLogEntry(1, 10, 20, DateTimeOffset.UtcNow) },
                CancellationToken.None);
        }

        (await db.ObservedMessages.CountAsync()).ShouldBe(0);
        (await db.MessageLogEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ObserveMessageAsync_PersistsMetadataOnly_AndSupportsDeleteAttribution()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        ModerationLoggingStore store = CreateStore();

        await store.ObserveMessageAsync(new ObservedMessage(10, 1, 2, 3, observedAt), CancellationToken.None);

        ObservedMessage? observed = await store.FindObservedMessageAsync(1, 10, CancellationToken.None);

        observed.ShouldNotBeNull();
        observed.OriginalMessageId.ShouldBe(10UL);
        observed.GuildId.ShouldBe(1UL);
        observed.ChannelId.ShouldBe(2UL);
        observed.AuthorId.ShouldBe(3UL);
        observed.ObservedAtUtc.ShouldBeInRange(observedAt.AddSeconds(-1), observedAt.AddSeconds(1));
    }

    [Test]
    public async Task ObserveMessageAsync_WhenMessageAlreadyObserved_PreservesOriginalObservation()
    {
        DateTimeOffset firstObservedAt = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondObservedAt = firstObservedAt.AddMinutes(5);
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        ModerationLoggingStore store = CreateStore();

        await store.ObserveMessageAsync(new ObservedMessage(10, 1, 2, 3, firstObservedAt), CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(10, 1, 4, 5, secondObservedAt), CancellationToken.None);

        List<ObservedMessage> observedMessages = await db.ObservedMessages.AsNoTracking().ToListAsync();

        observedMessages.Count.ShouldBe(1);
        observedMessages.Single().ObservedAtUtc.ShouldBe(firstObservedAt);
        observedMessages.Single().ChannelId.ShouldBe(2UL);
        observedMessages.Single().AuthorId.ShouldBe(3UL);
    }

    [Test]
    public async Task RecordLogEntriesAsync_AllowsBulkDeleteRowsPointingAtSameLogMessage()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        ModerationLoggingStore store = CreateStore();
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;

        await store.ObserveMessageAsync(new ObservedMessage(10, 1, 2, 3, capturedAt), CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(11, 1, 2, 4, capturedAt), CancellationToken.None);
        await store.RecordLogEntriesAsync(
            new[] { new MessageLogEntry(1, 10, 99, capturedAt), new MessageLogEntry(1, 11, 99, capturedAt) },
            CancellationToken.None
        );

        List<MessageLogEntry> entries = await db.MessageLogEntries.AsNoTracking()
            .OrderBy(entry => entry.OriginalMessageId).ToListAsync();

        entries.Count.ShouldBe(2);
        entries.Select(entry => entry.LogMessageId).Distinct().Single().ShouldBe(99UL);
    }

    [Test]
    public async Task DeleteExpiredMetadataAsync_DeletesExpiredRowsAndPreservesBoundaryAndRecentRows()
    {
        DateTimeOffset cutoff = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero);
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        ModerationLoggingStore store = CreateStore();

        await store.ObserveMessageAsync(new ObservedMessage(10, 1, 2, 3, cutoff.AddSeconds(-1)),
            CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(11, 1, 2, 3, cutoff), CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(12, 1, 2, 3, cutoff.AddSeconds(1)), CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(20, 1, 2, 3, cutoff.AddSeconds(-1)),
            CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(21, 1, 2, 3, cutoff), CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(22, 1, 2, 3, cutoff.AddSeconds(1)), CancellationToken.None);
        await store.RecordLogEntriesAsync(
            new[]
            {
                new MessageLogEntry(1, 20, 30, cutoff.AddSeconds(-1)),
                new MessageLogEntry(1, 21, 31, cutoff),
                new MessageLogEntry(1, 22, 32, cutoff.AddSeconds(1))
            },
            CancellationToken.None
        );

        int firstDeletedCount = await store.DeleteExpiredMetadataAsync(cutoff, CancellationToken.None);
        int secondDeletedCount = await store.DeleteExpiredMetadataAsync(cutoff, CancellationToken.None);

        firstDeletedCount.ShouldBe(3);
        secondDeletedCount.ShouldBe(0);
        (
            await db.ObservedMessages.AsNoTracking().Select(message => message.OriginalMessageId)
                .OrderBy(messageId => messageId).ToListAsync()
        ).ShouldBe([11UL, 12UL, 21UL, 22UL]);
        (await db.MessageLogEntries.AsNoTracking().Select(entry => entry.OriginalMessageId)
            .OrderBy(messageId => messageId).ToListAsync()).ShouldBe([
            21UL,
            22UL
        ]);
    }

    [Test]
    public async Task ObservedMessagesAndLogEntries_AreIsolatedByGuild()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        ModerationLoggingStore store = CreateStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await store.ObserveMessageAsync(new ObservedMessage(10, 1, 2, 3, now), CancellationToken.None);
        await store.ObserveMessageAsync(new ObservedMessage(10, 2, 4, 5, now), CancellationToken.None);
        await store.RecordLogEntriesAsync(
            new[] { new MessageLogEntry(1, 10, 99, now), new MessageLogEntry(2, 10, 99, now) }, CancellationToken.None);

        ObservedMessage? firstGuild = await store.FindObservedMessageAsync(1, 10, CancellationToken.None);
        ObservedMessage? secondGuild = await store.FindObservedMessageAsync(2, 10, CancellationToken.None);
        IReadOnlyDictionary<ulong, ObservedMessage> firstGuildBatch = await store
            .FindObservedMessagesAsync(1, new[] { 10UL }, CancellationToken.None)
            .ConfigureAwait(false);

        firstGuild.ShouldNotBeNull();
        secondGuild.ShouldNotBeNull();
        firstGuild.ChannelId.ShouldBe(2UL);
        secondGuild.ChannelId.ShouldBe(4UL);
        firstGuildBatch.Single().Value.GuildId.ShouldBe(1UL);
        (await db.MessageLogEntries.CountAsync(entry => entry.OriginalMessageId == 10 && entry.LogMessageId == 99))
            .ShouldBe(2);
    }

    [Test]
    public void PersistenceModel_DoesNotContainPrivateEvidenceOrDiscordIdentityFields()
    {
        using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        string[] forbiddenFragments =
            { "content", "filename", "url", "bytes", "username", "nick", "discriminator", "path" };

        string[] loggingColumns = db
            .Model.GetEntityTypes()
            .Where(entityType => string.Equals(entityType.ClrType.Namespace, typeof(LoggingConfiguration).Namespace!,
                StringComparison.Ordinal))
            .SelectMany(entityType => entityType.GetProperties())
            .Select(property => property.GetColumnName())
            .ToArray();

        loggingColumns.ShouldNotBeEmpty();

        foreach (string column in loggingColumns)
            forbiddenFragments.Any(fragment => column.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .ShouldBeFalse(column);
    }

    private static ModerationLoggingStore CreateStore()
    {
        return new ModerationLoggingStore(new PostgresBotDbContextFactory());
    }

    private sealed class PostgresBotDbContextFactory : IDbContextFactory<BotDbContext>
    {
        public BotDbContext CreateDbContext()
        {
            return PostgresDatabaseFixture.CreateDbContext();
        }
    }
}