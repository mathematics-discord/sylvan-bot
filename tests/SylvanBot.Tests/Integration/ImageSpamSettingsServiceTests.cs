using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SylvanBot.Application.Common;
using SylvanBot.Features.Moderation;

namespace SylvanBot.Tests.Integration;

[TestFixture]
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class ImageSpamSettingsServiceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
    }

    [Test]
    public async Task UpsertAsync_NewGuildWithUnspecifiedValues_PersistsAndPublishesDefaults()
    {
        // Arrange
        await using var                                            db              = PostgresDatabaseFixture.CreateDbContext();
        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new();
        var                                                        service         = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        var result = await service.UpsertAsync(guildId: 1, requiredChannelCount: null, requiredAttachmentCount: null, burstDurationSeconds: null, ct: CancellationToken.None);

        // Assert
        await using var readback  = PostgresDatabaseFixture.CreateDbContext();
        var             persisted = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 1);

        (persisted.RequiredChannelCount, persisted.RequiredAttachmentCount, persisted.BurstDurationSeconds)
           .ShouldBe((4, 2, 45));
        persisted.IsEnabled.ShouldBeTrue();
        (result.DistinctChannelThreshold, result.RequiredAttachmentCount, result.Window).ShouldBe((4, 2, 45));
        settingsByGuild.TryGetValue(key: 1, out var published).ShouldBeTrue();
        published.ShouldBe(result);
    }

    [TestCase(8, null, null, 8, 3, 60)]
    [TestCase(null, 7, null, 5, 7, 60)]
    [TestCase(null, null, 90, 5, 3, 90)]
    public async Task UpsertAsync_OneValueProvided_PreservesOtherValuesAndEnablesDetection(
        int? channels,
        int? attachments,
        int? seconds,
        int expectedChannels,
        int expectedAttachments,
        int expectedSeconds)
    {
        // Arrange
        await using var db       = PostgresDatabaseFixture.CreateDbContext();
        var             settings = ImageSpamSettings.CreateDefault(1);
        settings.Update(requiredChannelCount: 5, requiredAttachmentCount: 3, burstDurationSeconds: 60);
        settings.Disable();
        db.ImageSpamSettings.Add(settings);
        await db.SaveChangesAsync();
        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new();
        var                                                        service         = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        var result =
            await service.UpsertAsync(guildId: 1, requiredChannelCount: channels, requiredAttachmentCount: attachments, burstDurationSeconds: seconds, ct: CancellationToken.None);

        // Assert
        await using var readback  = PostgresDatabaseFixture.CreateDbContext();
        var             persisted = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 1);

        (persisted.RequiredChannelCount, persisted.RequiredAttachmentCount, persisted.BurstDurationSeconds)
           .ShouldBe((expectedChannels, expectedAttachments, expectedSeconds));
        persisted.IsEnabled.ShouldBeTrue();

        (result.DistinctChannelThreshold, result.RequiredAttachmentCount, result.Window)
           .ShouldBe((expectedChannels, expectedAttachments, expectedSeconds));
        settingsByGuild.TryGetValue(key: 1, out var published).ShouldBeTrue();
        published.ShouldBe(result);
    }

    [Test]
    public async Task GetCurrentAsync_EnabledSettings_ReturnsAndPublishesStoredValues()
    {
        // Arrange
        await using var db       = PostgresDatabaseFixture.CreateDbContext();
        var             settings = ImageSpamSettings.CreateDefault(1);
        settings.Update(requiredChannelCount: 6, requiredAttachmentCount: 3, burstDurationSeconds: 90);
        db.ImageSpamSettings.Add(settings);
        await db.SaveChangesAsync();
        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new();
        settingsByGuild[1] = new ImageBurstSpamDetectorOptions();
        var service = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        Option<ImageBurstSpamDetectorOptions> result = await service.GetCurrentAsync(guildId: 1, ct: CancellationToken.None);

        // Assert
        var current = result.Value.ShouldBeOfType<Some<ImageBurstSpamDetectorOptions>>().Value;
        current.DistinctChannelThreshold.ShouldBe(6);
        current.RequiredAttachmentCount.ShouldBe(3);
        current.Window.ShouldBe(90);
        settingsByGuild.TryGetValue(key: 1, out var published).ShouldBeTrue();
        published.ShouldBe(current);
    }

    [Test]
    public async Task GetCurrentAsync_AbsentSettings_ReturnsNoneAndRemovesOnlyRequestedGuildConfiguration()
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild   = new();
        var                                                        otherGuildOptions = new ImageBurstSpamDetectorOptions { Window = 90 };
        settingsByGuild[1] = new ImageBurstSpamDetectorOptions();
        settingsByGuild[2] = otherGuildOptions;
        var service = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        Option<ImageBurstSpamDetectorOptions> result = await service.GetCurrentAsync(guildId: 1, ct: CancellationToken.None);

        // Assert
        result.Value.ShouldBeOfType<None>();
        settingsByGuild.TryGetValue(key: 1, value: out _).ShouldBeFalse();
        settingsByGuild.TryGetValue(key: 2, out var otherGuild).ShouldBeTrue();
        otherGuild.ShouldBe(otherGuildOptions);
    }

    [Test]
    public async Task GetCurrentAsync_DisabledSettings_ReturnsNoneAndRemovesOnlyRequestedGuildConfiguration()
    {
        // Arrange
        await using var db       = PostgresDatabaseFixture.CreateDbContext();
        var             settings = ImageSpamSettings.CreateDefault(1);
        settings.Disable();
        db.ImageSpamSettings.Add(settings);
        await db.SaveChangesAsync();

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild   = new();
        var                                                        otherGuildOptions = new ImageBurstSpamDetectorOptions { Window = 90 };
        settingsByGuild[1] = new ImageBurstSpamDetectorOptions();
        settingsByGuild[2] = otherGuildOptions;
        var service = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        Option<ImageBurstSpamDetectorOptions> result = await service.GetCurrentAsync(guildId: 1, ct: CancellationToken.None);

        // Assert
        result.Value.ShouldBeOfType<None>();
        settingsByGuild.TryGetValue(key: 1, value: out _).ShouldBeFalse();
        settingsByGuild.TryGetValue(key: 2, out var otherGuild).ShouldBeTrue();
        otherGuild.ShouldBe(otherGuildOptions);
    }

    [Test]
    public async Task LoadEnabledSettingsAsync_EnabledAndDisabledGuilds_PublishesOnlyEnabledSettings()
    {
        // Arrange
        await using var db            = PostgresDatabaseFixture.CreateDbContext();
        var             firstGuild    = ImageSpamSettings.CreateDefault(1);
        var             disabledGuild = ImageSpamSettings.CreateDefault(2);
        disabledGuild.Disable();
        var thirdGuild = ImageSpamSettings.CreateDefault(3);
        thirdGuild.Update(requiredChannelCount: 6, requiredAttachmentCount: 3, burstDurationSeconds: 90);
        db.ImageSpamSettings.AddRange(firstGuild, disabledGuild, thirdGuild);
        await db.SaveChangesAsync();
        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new();
        var                                                        service         = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        await service.LoadEnabledSettingsAsync(CancellationToken.None);

        // Assert
        settingsByGuild.TryGetValue(key: 1, out var first).ShouldBeTrue();
        first.ShouldNotBeNull();
        (first.DistinctChannelThreshold, first.RequiredAttachmentCount, first.Window).ShouldBe((4, 2, 45));
        settingsByGuild.TryGetValue(key: 2, value: out _).ShouldBeFalse();
        settingsByGuild.TryGetValue(key: 3, out var third).ShouldBeTrue();
        third.ShouldNotBeNull();
        (third.DistinctChannelThreshold, third.RequiredAttachmentCount, third.Window).ShouldBe((6, 3, 90));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpsertAsync_SaveFails_DoesNotPublishUnpersistedSettings(bool hasExistingSettings)
    {
        // Arrange
        await using var                                            db              = PostgresDatabaseFixture.CreateDbContext();
        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new();

        if (hasExistingSettings)
        {
            db.ImageSpamSettings.Add(ImageSpamSettings.CreateDefault(1));
            await db.SaveChangesAsync();
            settingsByGuild[1] = new ImageBurstSpamDetectorOptions();
        }

        var service = new ImageSpamSettingsService(dbContext: db, settingsByGuild: settingsByGuild);

        // Act
        var exception = await Should.ThrowAsync<DbUpdateException>(() =>
                                                                       service.UpsertAsync(guildId: 1, requiredChannelCount: 0, requiredAttachmentCount: null, burstDurationSeconds: null, ct: CancellationToken.None));

        // Assert
        var postgresException = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgresException.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        postgresException.ConstraintName.ShouldBe("CK_ImageSpamSettings_RequiredChannelCount_Positive");
        await using var readback  = PostgresDatabaseFixture.CreateDbContext();
        var             persisted = await readback.ImageSpamSettings.SingleOrDefaultAsync(x => x.GuildId == 1);
        settingsByGuild.TryGetValue(key: 1, out var published).ShouldBe(hasExistingSettings);

        if (hasExistingSettings)
        {
            persisted.ShouldNotBeNull();

            (persisted.RequiredChannelCount, persisted.RequiredAttachmentCount, persisted.BurstDurationSeconds)
               .ShouldBe((4, 2, 45));
            persisted.IsEnabled.ShouldBeTrue();
            published.ShouldNotBeNull();

            (published.DistinctChannelThreshold, published.RequiredAttachmentCount, published.Window)
               .ShouldBe((4, 2, 45));
        }
        else
            persisted.ShouldBeNull();
    }
}