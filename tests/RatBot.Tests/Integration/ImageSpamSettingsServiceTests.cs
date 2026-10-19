using Microsoft.EntityFrameworkCore;
using Npgsql;
using RatBot.Features.Moderation;

namespace RatBot.Tests.Integration;

[TestFixture]
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
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var detectorSettings = new ImageBurstSpamDetectorSettings();
        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        var result = await service.UpsertAsync(1, null, null, null, CancellationToken.None);

        // Assert
        await using var readback = PostgresDatabaseFixture.CreateDbContext();
        var persisted = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 1);
        (persisted.RequiredChannelCount, persisted.RequiredAttachmentCount, persisted.BurstDurationSeconds)
            .ShouldBe((4, 2, 45));
        persisted.IsEnabled.ShouldBeTrue();
        (result.DistinctChannelThreshold, result.RequiredAttachmentCount, result.Window).ShouldBe((4, 2, 45));
        detectorSettings.TryGet(1, out var published).ShouldBeTrue();
        published.ShouldBe(result);
    }

    [TestCase(8, null, null, 8, 3, 60)]
    [TestCase(null, 7, null, 5, 7, 60)]
    [TestCase(null, null, 90, 5, 3, 90)]
    public async Task UpsertAsync_OneValueProvided_PreservesOtherValuesAndEnablesDetection(
        int? channels, int? attachments, int? seconds, int expectedChannels, int expectedAttachments,
        int expectedSeconds)
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var settings = ImageSpamSettings.CreateDefault(1);
        settings.Update(5, 3, 60);
        settings.Disable();
        db.ImageSpamSettings.Add(settings);
        await db.SaveChangesAsync();
        var detectorSettings = new ImageBurstSpamDetectorSettings();
        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        var result =
            await service.UpsertAsync(1, channels, attachments, seconds, CancellationToken.None);

        // Assert
        await using var readback = PostgresDatabaseFixture.CreateDbContext();
        var persisted = await readback.ImageSpamSettings.SingleAsync(x => x.GuildId == 1);
        (persisted.RequiredChannelCount, persisted.RequiredAttachmentCount, persisted.BurstDurationSeconds)
            .ShouldBe((expectedChannels, expectedAttachments, expectedSeconds));
        persisted.IsEnabled.ShouldBeTrue();
        (result.DistinctChannelThreshold, result.RequiredAttachmentCount, result.Window)
            .ShouldBe((expectedChannels, expectedAttachments, expectedSeconds));
        detectorSettings.TryGet(1, out var published).ShouldBeTrue();
        published.ShouldBe(result);
    }

    [Test]
    public async Task GetCurrentAsync_EnabledSettings_ReturnsAndPublishesStoredValues()
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var settings = ImageSpamSettings.CreateDefault(1);
        settings.Update(6, 3, 90);
        db.ImageSpamSettings.Add(settings);
        await db.SaveChangesAsync();
        var detectorSettings = new ImageBurstSpamDetectorSettings();
        detectorSettings.Update(1, new ImageBurstSpamDetectorOptions());
        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        Option<ImageBurstSpamDetectorOptions> result = await service.GetCurrentAsync(1, CancellationToken.None);

        // Assert
        var current = result.Value.ShouldBeOfType<Some<ImageBurstSpamDetectorOptions>>().Value;
        current.DistinctChannelThreshold.ShouldBe(6);
        current.RequiredAttachmentCount.ShouldBe(3);
        current.Window.ShouldBe(90);
        detectorSettings.TryGet(1, out var published).ShouldBeTrue();
        published.ShouldBe(current);
    }

    [Test]
    public async Task GetCurrentAsync_AbsentSettings_ReturnsNoneAndRemovesOnlyRequestedGuildConfiguration()
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();

        var detectorSettings = new ImageBurstSpamDetectorSettings();
        var otherGuildOptions = new ImageBurstSpamDetectorOptions { Window = 90 };
        detectorSettings.Update(1, new ImageBurstSpamDetectorOptions());
        detectorSettings.Update(2, otherGuildOptions);
        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        Option<ImageBurstSpamDetectorOptions> result = await service.GetCurrentAsync(1, CancellationToken.None);

        // Assert
        result.Value.ShouldBeOfType<None>();
        detectorSettings.TryGet(1, out _).ShouldBeFalse();
        detectorSettings.TryGet(2, out var otherGuild).ShouldBeTrue();
        otherGuild.ShouldBe(otherGuildOptions);
    }

    [Test]
    public async Task GetCurrentAsync_DisabledSettings_ReturnsNoneAndRemovesOnlyRequestedGuildConfiguration()
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var settings = ImageSpamSettings.CreateDefault(1);
        settings.Disable();
        db.ImageSpamSettings.Add(settings);
        await db.SaveChangesAsync();

        var detectorSettings = new ImageBurstSpamDetectorSettings();
        var otherGuildOptions = new ImageBurstSpamDetectorOptions { Window = 90 };
        detectorSettings.Update(1, new ImageBurstSpamDetectorOptions());
        detectorSettings.Update(2, otherGuildOptions);
        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        Option<ImageBurstSpamDetectorOptions> result = await service.GetCurrentAsync(1, CancellationToken.None);

        // Assert
        result.Value.ShouldBeOfType<None>();
        detectorSettings.TryGet(1, out _).ShouldBeFalse();
        detectorSettings.TryGet(2, out var otherGuild).ShouldBeTrue();
        otherGuild.ShouldBe(otherGuildOptions);
    }

    [Test]
    public async Task LoadEnabledSettingsAsync_EnabledAndDisabledGuilds_PublishesOnlyEnabledSettings()
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var firstGuild = ImageSpamSettings.CreateDefault(1);
        var disabledGuild = ImageSpamSettings.CreateDefault(2);
        disabledGuild.Disable();
        var thirdGuild = ImageSpamSettings.CreateDefault(3);
        thirdGuild.Update(6, 3, 90);
        db.ImageSpamSettings.AddRange(firstGuild, disabledGuild, thirdGuild);
        await db.SaveChangesAsync();
        var detectorSettings = new ImageBurstSpamDetectorSettings();
        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        await service.LoadEnabledSettingsAsync(CancellationToken.None);

        // Assert
        detectorSettings.TryGet(1, out var first).ShouldBeTrue();
        (first.DistinctChannelThreshold, first.RequiredAttachmentCount, first.Window).ShouldBe((4, 2, 45));
        detectorSettings.TryGet(2, out _).ShouldBeFalse();
        detectorSettings.TryGet(3, out var third).ShouldBeTrue();
        (third.DistinctChannelThreshold, third.RequiredAttachmentCount, third.Window).ShouldBe((6, 3, 90));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpsertAsync_SaveFails_DoesNotPublishUnpersistedSettings(bool hasExistingSettings)
    {
        // Arrange
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        var detectorSettings = new ImageBurstSpamDetectorSettings();
        if (hasExistingSettings)
        {
            db.ImageSpamSettings.Add(ImageSpamSettings.CreateDefault(1));
            await db.SaveChangesAsync();
            detectorSettings.Update(1, new ImageBurstSpamDetectorOptions());
        }

        var service = new ImageSpamSettingsService(db, detectorSettings);

        // Act
        var exception = await Should.ThrowAsync<DbUpdateException>(() =>
            service.UpsertAsync(1, 0, null, null, CancellationToken.None));

        // Assert
        var postgresException = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgresException.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        postgresException.ConstraintName.ShouldBe("CK_ImageSpamSettings_RequiredChannelCount_Positive");
        await using var readback = PostgresDatabaseFixture.CreateDbContext();
        var persisted = await readback.ImageSpamSettings.SingleOrDefaultAsync(x => x.GuildId == 1);
        detectorSettings.TryGet(1, out var published).ShouldBe(hasExistingSettings);
        if (hasExistingSettings)
        {
            persisted.ShouldNotBeNull();
            (persisted.RequiredChannelCount, persisted.RequiredAttachmentCount, persisted.BurstDurationSeconds)
                .ShouldBe((4, 2, 45));
            persisted.IsEnabled.ShouldBeTrue();
            (published.DistinctChannelThreshold, published.RequiredAttachmentCount, published.Window)
                .ShouldBe((4, 2, 45));
        }
        else
        {
            persisted.ShouldBeNull();
        }
    }
}