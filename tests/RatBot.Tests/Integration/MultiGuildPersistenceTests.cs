using Microsoft.EntityFrameworkCore;
using RatBot.Features.EmojiAnalytics.Persistence;
using RatBot.Features.EmojiAnalytics;
using RatBot.Features.Moderation;
using RatBot.Features.RoleColours;
using RatBot.Infrastructure.Data;

namespace RatBot.Tests.Integration;

[TestFixture]
public sealed class MultiGuildPersistenceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
    }

    [Test]
    public async Task RoleColourState_IsIsolatedByGuild()
    {
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var operations = new RoleColourOperations(db);

        var guildA = (await operations.UpsertMappingAsync(1, null, 10, 20, CancellationToken.None))
            .Option;
        var guildB = (await operations.UpsertMappingAsync(2, null, 10, 20, CancellationToken.None))
            .Option;

        (await operations.ListConfiguredOptionsAsync(1, CancellationToken.None)).Single().OptionId
            .ShouldBe(guildA.OptionId);
        (await operations.ListConfiguredOptionsAsync(2, CancellationToken.None)).Single().OptionId
            .ShouldBe(guildB.OptionId);

        await operations.DeleteMappingAsync(1, guildA.OptionId, CancellationToken.None);

        (await operations.ListConfiguredOptionsAsync(1, CancellationToken.None)).ShouldBeEmpty();
        (await operations.ListConfiguredOptionsAsync(2, CancellationToken.None)).Single().OptionId
            .ShouldBe(guildB.OptionId);
    }

    [Test]
    public async Task UpsertAsync_DifferentGuilds_KeepsPersistedAndPublishedSettingsSeparate()
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var guildA = ImageSpamSettings.CreateDefault(1);
        db.ImageSpamSettings.Add(guildA);
        await db.SaveChangesAsync();
        var detectorSettings = new ImageBurstSpamDetectorSettings();
        detectorSettings.Update(1, new ImageBurstSpamDetectorOptions());
        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        await service.UpsertAsync(2, 2, 5, 60, CancellationToken.None);

        // Assert
        await using var readback = PostgresDatabaseFixture.CreateDbContext();
        var persistedA = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 1);
        var persistedB = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 2);
        (persistedA.RequiredChannelCount, persistedA.RequiredAttachmentCount, persistedA.BurstDurationSeconds)
            .ShouldBe((4, 2, 45));
        (persistedB.RequiredChannelCount, persistedB.RequiredAttachmentCount, persistedB.BurstDurationSeconds)
            .ShouldBe((2, 5, 60));
        detectorSettings.TryGet(1, out var publishedA).ShouldBeTrue();
        detectorSettings.TryGet(2, out var publishedB).ShouldBeTrue();
        (publishedA.DistinctChannelThreshold, publishedA.RequiredAttachmentCount, publishedA.Window)
            .ShouldBe((4, 2, 45));
        (publishedB.DistinctChannelThreshold, publishedB.RequiredAttachmentCount, publishedB.Window)
            .ShouldBe((2, 5, 60));
    }

    [Test]
    public async Task EmojiState_IsPerGuild()
    {
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        db.EmojiUsageCounts.Add(
            new EmojiUsageCount
            {
                GuildId = 1,
                EmojiId = 500,
                MessageUsageCount = 1,
                ReactionUsageCount = 1,
            }
        );
        db.EmojiUsageCounts.Add(
            new EmojiUsageCount
            {
                GuildId = 2,
                EmojiId = 500,
                MessageUsageCount = 3,
                ReactionUsageCount = 5,
            }
        );
        await db.SaveChangesAsync();

        var emojiUsageStore = new EmojiUsageStore(new EmojiDbContextFactory());
        var guild1Usage = await emojiUsageStore.GetUsagePageAsync(1, [500UL], 1, ct: CancellationToken.None);
        var guild2Usage = await emojiUsageStore.GetUsagePageAsync(2, [500UL], 1, ct: CancellationToken.None);
        var firstPage = guild1Usage.Value.ShouldBeOfType<Some<EmojiUsagePage>>().Value;
        var secondPage = guild2Usage.Value.ShouldBeOfType<Some<EmojiUsagePage>>().Value;
        firstPage.Items
            .Single().ReactionUsageCount.ShouldBe(1);
        secondPage.Items
            .Single().ReactionUsageCount.ShouldBe(5);

        Option<EmojiUsagePage> pruningRead =
            await emojiUsageStore.GetUsagePageAsync(1, [501UL], 1, ct: CancellationToken.None);

        pruningRead.Value.ShouldBeOfType<None>();
        (await db.EmojiUsageCounts.AnyAsync(x => x.GuildId == 1 && x.EmojiId == 500)).ShouldBeTrue();

        await emojiUsageStore.RecordBatchAsync(1, [new EmojiUsageIncrement(1, 501, 0, 1)], [501UL],
            CancellationToken.None);

        (await db.EmojiUsageCounts.AnyAsync(x => x.GuildId == 1 && x.EmojiId == 500)).ShouldBeFalse();
        var guild2Emoji = await db.EmojiUsageCounts.SingleAsync(x => x.GuildId == 2 && x.EmojiId == 500);
        guild2Emoji.MessageUsageCount.ShouldBe(3);
        guild2Emoji.ReactionUsageCount.ShouldBe(5);
        (await db.EmojiUsageCounts.SingleAsync(x => x.GuildId == 1 && x.EmojiId == 501)).ReactionUsageCount.ShouldBe(1);
    }

    [Test]
    public async Task EmojiUsage_AggregatesMessageAndReactionIncrements()
    {
        const ulong emojiId = 12345678901234567;

        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var emojiUsageStore = new EmojiUsageStore(new EmojiDbContextFactory());
        await emojiUsageStore.RecordBatchAsync(
            1,
            [new EmojiUsageIncrement(1, emojiId, 2, 0), new EmojiUsageIncrement(1, emojiId, 0, 3)],
            [emojiId],
            CancellationToken.None
        );

        var usage = await db.EmojiUsageCounts.SingleAsync(x => x.GuildId == 1 && x.EmojiId == emojiId);
        usage.MessageUsageCount.ShouldBe(2);
        usage.ReactionUsageCount.ShouldBe(3);
    }

    private sealed class EmojiDbContextFactory : IDbContextFactory<BotDbContext>
    {
        public BotDbContext CreateDbContext()
        {
            return PostgresDatabaseFixture.CreateDbContext();
        }
    }
}