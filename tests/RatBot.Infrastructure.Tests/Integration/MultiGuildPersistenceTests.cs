using Microsoft.EntityFrameworkCore;
using RatBot.Features.EmojiAnalytics;
using RatBot.Features.RoleColours;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Stores;

namespace RatBot.Infrastructure.Tests.Integration;

[TestFixture]
public sealed class MultiGuildPersistenceTests
{
    [SetUp]
    public async Task SetUp() => await PostgresDatabaseFixture.ResetAsync();

    [Test]
    public async Task RoleColourState_IsIsolatedByGuild()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        RoleColourOperations operations = new RoleColourOperations(db);

        RoleColourOption guildA = (await operations.UpsertMappingAsync(1, null, 10, 20, CancellationToken.None)).Value.Option;
        RoleColourOption guildB = (await operations.UpsertMappingAsync(2, null, 10, 20, CancellationToken.None)).Value.Option;

        (await operations.ListConfiguredOptionsAsync(1, CancellationToken.None)).Single().OptionId.ShouldBe(guildA.OptionId);
        (await operations.ListConfiguredOptionsAsync(2, CancellationToken.None)).Single().OptionId.ShouldBe(guildB.OptionId);

        (await operations.DeleteMappingAsync(1, guildA.OptionId, CancellationToken.None)).IsError.ShouldBeFalse();

        (await operations.ListConfiguredOptionsAsync(1, CancellationToken.None)).ShouldBeEmpty();
        (await operations.ListConfiguredOptionsAsync(2, CancellationToken.None)).Single().OptionId.ShouldBe(guildB.OptionId);
    }

    [Test]
    public async Task ImageSpamSettings_ArePerGuild()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        ImageSpamSettingsStore imageStore = new ImageSpamSettingsStore(db);

        await imageStore.UpsertAsync(1, 4, 2, 45, CancellationToken.None);
        await imageStore.UpsertAsync(2, 2, 5, 60, CancellationToken.None);

        (await imageStore.GetAsync(1, CancellationToken.None))!.RequiredChannelCount.ShouldBe(4);
        (await imageStore.GetAsync(2, CancellationToken.None))!.RequiredAttachmentCount.ShouldBe(5);
    }

    [Test]
    public async Task EmojiState_IsPerGuild()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
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

        EmojiUsageStore emojiUsageStore = new EmojiUsageStore(new EmojiDbContextFactory());
        (await emojiUsageStore.GetUsagePageAsync(1, [500UL], 1, ct: CancellationToken.None)).Value.Items.Single().ReactionUsageCount.ShouldBe(1);
        (await emojiUsageStore.GetUsagePageAsync(2, [500UL], 1, ct: CancellationToken.None)).Value.Items.Single().ReactionUsageCount.ShouldBe(5);

        ErrorOr<EmojiUsagePage> pruningRead = await emojiUsageStore.GetUsagePageAsync(1, [501UL], 1, ct: CancellationToken.None);

        pruningRead.FirstError.Type.ShouldBe(ErrorType.NotFound);
        (await db.EmojiUsageCounts.AnyAsync(x => x.GuildId == 1 && x.EmojiId == 500)).ShouldBeTrue();

        await emojiUsageStore.RecordBatchAsync(1, [new EmojiUsageIncrement(1, 501, 0, 1)], [501UL], CancellationToken.None);

        (await db.EmojiUsageCounts.AnyAsync(x => x.GuildId == 1 && x.EmojiId == 500)).ShouldBeFalse();
        EmojiUsageCount guild2Emoji = await db.EmojiUsageCounts.SingleAsync(x => x.GuildId == 2 && x.EmojiId == 500);
        guild2Emoji.MessageUsageCount.ShouldBe(3);
        guild2Emoji.ReactionUsageCount.ShouldBe(5);
        (await db.EmojiUsageCounts.SingleAsync(x => x.GuildId == 1 && x.EmojiId == 501)).ReactionUsageCount.ShouldBe(1);
    }

    [Test]
    public async Task EmojiUsage_AggregatesMessageAndReactionIncrements()
    {
        const ulong emojiId = 12345678901234567;

        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        EmojiUsageStore emojiUsageStore = new EmojiUsageStore(new EmojiDbContextFactory());
        await emojiUsageStore.RecordBatchAsync(
            1,
            [new EmojiUsageIncrement(1, emojiId, 2, 0), new EmojiUsageIncrement(1, emojiId, 0, 3)],
            [emojiId],
            CancellationToken.None
        );

        EmojiUsageCount usage = await db.EmojiUsageCounts.SingleAsync(x => x.GuildId == 1 && x.EmojiId == emojiId);
        usage.MessageUsageCount.ShouldBe(2);
        usage.ReactionUsageCount.ShouldBe(3);
    }

    private sealed class EmojiDbContextFactory : IDbContextFactory<BotDbContext>
    {
        public BotDbContext CreateDbContext() => PostgresDatabaseFixture.CreateDbContext();
    }
}
