using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using SylvanBot.Features.Moderation;

namespace SylvanBot.Tests.Features.Moderation;

[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class ImageBurstSpamDetectorTests
{
    private static readonly DateTimeOffset BaseTimestamp = new DateTimeOffset(
        year: 2026,
        month: 5,
        day: 31,
        hour: 12,
        minute: 0,
        second: 0,
        offset: TimeSpan.Zero);

    [Test]
    public void Observe_AfterDetection_DoesNotTriggerAgainWhileHandlingLockIsActive()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp), new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(1)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(2)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        Optional<ImageBurstDetection> initial = detector.Observe(
            CreateMessage(channelId: 40, timestamp: BaseTimestamp.AddSeconds(3)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        initial.HasValue.ShouldBeTrue();

        // Act
        Optional<ImageBurstDetection> result = detector.Observe(
            CreateMessage(channelId: 50, timestamp: BaseTimestamp.AddSeconds(4)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public void Observe_WithFourDistinctChannelsInsideWindow_ReturnsDetection()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp), new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(10)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(20)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        // Act
        Optional<ImageBurstDetection> result = detector.Observe(
            CreateMessage(channelId: 40, timestamp: BaseTimestamp.AddSeconds(30)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        // Assert
        var detection = result.Value;
        detection.GuildId.ShouldBe(1UL);
        detection.UserId.ShouldBe(2UL);
        detection.ChannelIds.ShouldBe([10UL, 20UL, 30UL, 40UL]);
        detection.Messages.Count.ShouldBe(4);
    }

    [Test]
    public void Observe_WithOldMessages_PrunesOutsideWindow()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp), new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(1)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(2)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        // Act
        Optional<ImageBurstDetection> result = detector.Observe(
            CreateMessage(channelId: 40, timestamp: BaseTimestamp.AddSeconds(46)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public void Observe_WithRepeatedChannel_DoesNotIncreaseChannelCount()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp), new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 10, timestamp: BaseTimestamp.AddSeconds(5)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(10)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(15)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        // Act
        Optional<ImageBurstDetection> result = detector.Observe(
            CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(20)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public void Observe_WithSameUserInDifferentGuilds_DoesNotShareWindow()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        settingsByGuild[2] = settingsByGuild[1];

        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp), new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(1)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(guildId: 2, channelId: 30, timestamp: BaseTimestamp.AddSeconds(2)),
            settingsByGuild.TryGetValue(key: 2, out var secondOptions) ? new Optional<ImageBurstSpamDetectorOptions>(secondOptions) : default);

        detector.Observe(
            CreateMessage(guildId: 2, channelId: 40, timestamp: BaseTimestamp.AddSeconds(3)),
            settingsByGuild.TryGetValue(key: 2, out var otherOptions) ? new Optional<ImageBurstSpamDetectorOptions>(otherOptions) : default);

        // Act
        Optional<ImageBurstDetection> result = detector.Observe(
            CreateMessage(channelId: 50, timestamp: BaseTimestamp.AddSeconds(4)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public void Observe_GuildHasNoSettings_ReturnsNone()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);
        detector.Observe(CreateMessage(channelId: 10), settings: default);
        detector.Observe(CreateMessage(channelId: 20), settings: default);
        detector.Observe(CreateMessage(channelId: 30), settings: default);

        // Act
        Optional<ImageBurstDetection> result = detector.Observe(CreateMessage(channelId: 40), settings: default);

        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public void Observe_GuildsHaveDifferentThresholds_UsesRequestedGuildThreshold()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>
        {
            [1] = new ImageBurstSpamDetectorOptions { DistinctChannelThreshold = 4 },
            [2] = new ImageBurstSpamDetectorOptions { DistinctChannelThreshold = 2 },
        };
        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);
        detector.Observe(CreateMessage(guildId: 1, channelId: 10), new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        detector.Observe(CreateMessage(guildId: 2, channelId: 10), new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[2]));

        // Act
        Optional<ImageBurstDetection> firstGuildResult = detector.Observe(
            CreateMessage(guildId: 1, channelId: 20),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        Optional<ImageBurstDetection> secondGuildResult = detector.Observe(
            CreateMessage(guildId: 2, channelId: 20),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[2]));
        // Assert
        firstGuildResult.IsUndefined.ShouldBeTrue();
        var detection = secondGuildResult.Value;
        detection.GuildId.ShouldBe(2UL);
        detection.ChannelIds.ShouldBe([10UL, 20UL]);
    }

    [Test]
    public void Observe_WithThreeQualifyingMessagesAcrossThreeChannelsInsideFifteenSeconds_ReturnsDetection()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 15,
                DistinctChannelThreshold = 3,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider: timeProvider);

        detector.Observe(
            CreateMessage(channelId: 908076393198419988, timestamp: BaseTimestamp),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        detector.Observe(
            CreateMessage(channelId: 903481102591750184, timestamp: BaseTimestamp.AddMilliseconds(2161)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));

        // Act
        Optional<ImageBurstDetection> result = detector.Observe(
            CreateMessage(channelId: 486841085210132490, timestamp: BaseTimestamp.AddMilliseconds(4362)),
            new Optional<ImageBurstSpamDetectorOptions>(settingsByGuild[1]));
        // Assert
        var detection = result.Value;
        detection.ChannelIds.ShouldBe([486841085210132490UL, 903481102591750184UL, 908076393198419988UL]);
        detection.Messages.Count.ShouldBe(3);
    }

    private static ImageBurstMessage CreateMessage(ulong guildId = 1, ulong userId = 2, ulong channelId = 10, DateTimeOffset? timestamp = null) =>
        new ImageBurstMessage(
            GuildId: guildId,
            UserId: userId,
            ChannelId: channelId,
            timestamp ?? BaseTimestamp,
            [new ImageBurstAttachment("https://cdn.example/a.png"), new ImageBurstAttachment("https://cdn.example/b.png")]);

    private sealed class TestTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}