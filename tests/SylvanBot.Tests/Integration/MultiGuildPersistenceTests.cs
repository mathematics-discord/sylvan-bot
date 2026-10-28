using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
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
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var operations = new RoleColourOperations(db);

        var guildA = (
                await operations.UpsertMappingAsync(guildId: 1, optionId: null, sourceRoleId: 10, displayRoleId: 20, ct: CancellationToken.None))
            .Value
            .Option;

        var guildB = (
                await operations.UpsertMappingAsync(guildId: 2, optionId: null, sourceRoleId: 10, displayRoleId: 20, ct: CancellationToken.None))
            .Value
            .Option;

        (await operations.ListConfiguredOptionsAsync(guildId: 1, ct: CancellationToken.None)).Single().OptionId.ShouldBe(guildA.OptionId);

        (await operations.ListConfiguredOptionsAsync(guildId: 2, ct: CancellationToken.None)).Single().OptionId.ShouldBe(guildB.OptionId);

        (await operations.DeleteMappingAsync(guildId: 1, optionId: guildA.OptionId, ct: CancellationToken.None)).IsSuccessful.ShouldBeTrue();

        (await operations.ListConfiguredOptionsAsync(guildId: 1, ct: CancellationToken.None)).ShouldBeEmpty();

        (await operations.ListConfiguredOptionsAsync(guildId: 2, ct: CancellationToken.None)).Single().OptionId.ShouldBe(guildB.OptionId);
    }

    [Test]
    public async Task UpsertAsync_DifferentGuilds_KeepsPersistedAndPublishedSettingsSeparate()
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var guildA = ImageSpamSettings.CreateDefault(1);
        db.ImageSpamSettings.Add(guildA);
        await db.SaveChangesAsync();

        var service = new ImageSpamSettingsService(PostgresDatabaseFixture.CreateContextFactory());
        await service.LoadEnabledSettingsAsync(CancellationToken.None);

        // Act
        await service.UpsertAsync(
            guildId: 2,
            requiredChannelCount: 2,
            requiredAttachmentCount: 5,
            burstDurationSeconds: 60,
            ct: CancellationToken.None);
        // Assert
        await using var readback = PostgresDatabaseFixture.CreateDbContext();

        var persistedA = await readback.ImageSpamSettings
            .SingleAsync(x => x.GuildId == 1);

        var persistedB = await readback.ImageSpamSettings
            .SingleAsync(x => x.GuildId == 2);

        (persistedA.RequiredChannelCount, persistedA.RequiredAttachmentCount, persistedA.BurstDurationSeconds).ShouldBe((4, 2, 45));

        (persistedB.RequiredChannelCount, persistedB.RequiredAttachmentCount, persistedB.BurstDurationSeconds).ShouldBe((2, 5, 60));
        var publishedA = service.Current(1).Value;
        var publishedB = service.Current(2).Value;
        publishedA.ShouldNotBeNull();
        publishedB.ShouldNotBeNull();

        (publishedA.DistinctChannelThreshold, publishedA.RequiredAttachmentCount, publishedA.Window).ShouldBe((4, 2, 45));

        (publishedB.DistinctChannelThreshold, publishedB.RequiredAttachmentCount, publishedB.Window).ShouldBe((2, 5, 60));
    }

}
