using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Features.EmojiAnalytics;
using SylvanBot.Features.EmojiAnalytics.Persistence;
using SylvanBot.Features.Moderation;
using SylvanBot.Features.RoleColours;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Tests.Integration;

[TestFixture]
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
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
        await using var db         = PostgresDatabaseFixture.CreateDbContext();
        var             operations = new RoleColourOperations(db);

        var guildA = (await operations.UpsertMappingAsync(guildId: 1, optionId: null, sourceRoleId: 10, displayRoleId: 20, ct: CancellationToken.None)).Value
           .Option;

        var guildB = (await operations.UpsertMappingAsync(guildId: 2, optionId: null, sourceRoleId: 10, displayRoleId: 20, ct: CancellationToken.None)).Value
           .Option;

        (await operations.ListConfiguredOptionsAsync(guildId: 1, ct: CancellationToken.None)).Single().OptionId
           .ShouldBe(guildA.OptionId);

        (await operations.ListConfiguredOptionsAsync(guildId: 2, ct: CancellationToken.None)).Single().OptionId
           .ShouldBe(guildB.OptionId);

        (await operations.DeleteMappingAsync(guildId: 1, optionId: guildA.OptionId, ct: CancellationToken.None)).IsError.ShouldBeFalse();

        (await operations.ListConfiguredOptionsAsync(guildId: 1, ct: CancellationToken.None)).ShouldBeEmpty();

        (await operations.ListConfiguredOptionsAsync(guildId: 2, ct: CancellationToken.None)).Single().OptionId
           .ShouldBe(guildB.OptionId);
    }

    [Test]
    public async Task UpsertAsync_DifferentGuilds_KeepsPersistedAndPublishedSettingsSeparate()
    {
        // Arrange
        await using var db     = PostgresDatabaseFixture.CreateDbContext();
        var             guildA = ImageSpamSettings.CreateDefault(1);
        db.ImageSpamSettings.Add(guildA);
        await db.SaveChangesAsync();

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
        {
            [1] = new ImageBurstSpamDetectorOptions(),
        };

        var service = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        await service.UpsertAsync(guildId: 2, requiredChannelCount: 2, requiredAttachmentCount: 5, burstDurationSeconds: 60, ct: CancellationToken.None);

        // Assert
        await using var readback   = PostgresDatabaseFixture.CreateDbContext();
        var             persistedA = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 1);
        var             persistedB = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 2);

        (persistedA.RequiredChannelCount, persistedA.RequiredAttachmentCount, persistedA.BurstDurationSeconds)
           .ShouldBe((4, 2, 45));

        (persistedB.RequiredChannelCount, persistedB.RequiredAttachmentCount, persistedB.BurstDurationSeconds)
           .ShouldBe((2, 5, 60));
        settingsByGuild.TryGetValue(key: 1, out var publishedA).ShouldBeTrue();
        settingsByGuild.TryGetValue(key: 2, out var publishedB).ShouldBeTrue();
        publishedA.ShouldNotBeNull();
        publishedB.ShouldNotBeNull();

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
                GuildId            = 1,
                EmojiId            = 500,
                MessageUsageCount  = 1,
                ReactionUsageCount = 1,
            });

        db.EmojiUsageCounts.Add(
            new EmojiUsageCount
            {
                GuildId            = 2,
                EmojiId            = 500,
                MessageUsageCount  = 3,
                ReactionUsageCount = 5,
            });
        await db.SaveChangesAsync();

        var emojiUsageStore = new EmojiUsageStore(new EmojiDbContextFactory());

        (await emojiUsageStore.GetUsagePageAsync(guildId: 1, [500UL], page: 1, ct: CancellationToken.None)).Value.Items.Single()
           .ReactionUsageCount.ShouldBe(1);

        (await emojiUsageStore.GetUsagePageAsync(guildId: 2, [500UL], page: 1, ct: CancellationToken.None)).Value.Items.Single()
           .ReactionUsageCount.ShouldBe(5);

        ErrorOr<EmojiUsagePage> pruningRead =
            await emojiUsageStore.GetUsagePageAsync(guildId: 1, [501UL], page: 1, ct: CancellationToken.None);

        pruningRead.FirstError.Type.ShouldBe(ErrorType.NotFound);
        (await db.EmojiUsageCounts.AnyAsync(x => x.GuildId == 1 && x.EmojiId == 500)).ShouldBeTrue();

        await emojiUsageStore.RecordBatchAsync(
            guildId: 1,
            [new EmojiUsageIncrement(GuildId: 1, EmojiId: 501, MessageCount: 0, ReactionCount: 1)],
            [501UL],
            ct: CancellationToken.None);

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

        await using var db              = PostgresDatabaseFixture.CreateDbContext();
        var             emojiUsageStore = new EmojiUsageStore(new EmojiDbContextFactory());

        await emojiUsageStore.RecordBatchAsync(
            guildId: 1,
            [new EmojiUsageIncrement(GuildId: 1, EmojiId: emojiId, MessageCount: 2, ReactionCount: 0), new EmojiUsageIncrement(GuildId: 1, EmojiId: emojiId, MessageCount: 0, ReactionCount: 3)],
            [emojiId],
            ct: CancellationToken.None);

        var usage = await db.EmojiUsageCounts.SingleAsync(x => x.GuildId == 1 && x.EmojiId == emojiId);
        usage.MessageUsageCount.ShouldBe(2);
        usage.ReactionUsageCount.ShouldBe(3);
    }

    private sealed class EmojiDbContextFactory : IDbContextFactory<BotDbContext>
    {
        public BotDbContext CreateDbContext() => PostgresDatabaseFixture.CreateDbContext();
    }
}