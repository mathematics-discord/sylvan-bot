namespace SylvanBot.Tests.Features.Moderation;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using SylvanBot.Application.Common;
using SylvanBot.Common;
using SylvanBot.Features.Moderation;

[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class ImageBurstSpamDetectorTests
{
    private static readonly DateTimeOffset BaseTimestamp = new(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Observe_AfterDetection_DoesNotTriggerAgainWhileHandlingLockIsActive()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp));
        detector.Observe(CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(1)));
        detector.Observe(CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(2)));

        Option<ImageBurstDetection> initial = detector.Observe(
            CreateMessage(channelId: 40, timestamp: BaseTimestamp.AddSeconds(3)));
        initial.Value.ShouldBeOfType<Some<ImageBurstDetection>>();

        // Act
        Option<ImageBurstDetection> result =
            detector.Observe(CreateMessage(channelId: 50, timestamp: BaseTimestamp.AddSeconds(4)));

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public void Observe_WithFourDistinctChannelsInsideWindow_ReturnsDetection()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp));
        detector.Observe(CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(10)));
        detector.Observe(CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(20)));

        // Act
        Option<ImageBurstDetection> result =
            detector.Observe(CreateMessage(channelId: 40, timestamp: BaseTimestamp.AddSeconds(30)));

        // Assert
        var detection = result.Value.ShouldBeOfType<Some<ImageBurstDetection>>().Value;
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

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp));
        detector.Observe(CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(1)));
        detector.Observe(CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(2)));

        // Act
        Option<ImageBurstDetection> result =
            detector.Observe(CreateMessage(channelId: 40, timestamp: BaseTimestamp.AddSeconds(46)));

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    private static ImageBurstMessage CreateMessage(
        ulong guildId = 1,
        ulong userId = 2,
        ulong channelId = 10,
        DateTimeOffset? timestamp = null)
    {
        return new ImageBurstMessage(
            guildId,
            userId,
            channelId,
            timestamp ?? BaseTimestamp,
            [
                new ImageBurstAttachment("https://cdn.example/a.png"),
                new ImageBurstAttachment("https://cdn.example/b.png"),
            ]);
    }

    [Test]
    public void Observe_WithRepeatedChannel_DoesNotIncreaseChannelCount()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 45,
                DistinctChannelThreshold = 4,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp));
        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp.AddSeconds(5)));
        detector.Observe(CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(10)));
        detector.Observe(CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(15)));

        // Act
        Option<ImageBurstDetection> result =
            detector.Observe(CreateMessage(channelId: 30, timestamp: BaseTimestamp.AddSeconds(20)));

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public void Observe_WithSameUserInDifferentGuilds_DoesNotShareWindow()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
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

        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);

        detector.Observe(CreateMessage(channelId: 10, timestamp: BaseTimestamp));
        detector.Observe(CreateMessage(channelId: 20, timestamp: BaseTimestamp.AddSeconds(1)));
        detector.Observe(CreateMessage(2, channelId: 30, timestamp: BaseTimestamp.AddSeconds(2)));
        detector.Observe(CreateMessage(2, channelId: 40, timestamp: BaseTimestamp.AddSeconds(3)));

        // Act
        Option<ImageBurstDetection> result = detector
            .Observe(CreateMessage(channelId: 50, timestamp: BaseTimestamp.AddSeconds(4)));

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public void Observe_GuildHasNoSettings_ReturnsNone()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new();
        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);
        detector.Observe(CreateMessage(channelId: 10));
        detector.Observe(CreateMessage(channelId: 20));
        detector.Observe(CreateMessage(channelId: 30));

        // Act
        Option<ImageBurstDetection> result = detector.Observe(CreateMessage(channelId: 40));

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public void Observe_GuildsHaveDifferentThresholds_UsesRequestedGuildThreshold()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
        {
            [1] = new ImageBurstSpamDetectorOptions { DistinctChannelThreshold = 4 },
            [2] = new ImageBurstSpamDetectorOptions { DistinctChannelThreshold = 2 },
        };
        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);
        detector.Observe(CreateMessage(guildId: 1, channelId: 10));
        detector.Observe(CreateMessage(guildId: 2, channelId: 10));

        // Act
        Option<ImageBurstDetection> firstGuildResult = detector.Observe(CreateMessage(guildId: 1, channelId: 20));
        Option<ImageBurstDetection> secondGuildResult = detector.Observe(CreateMessage(guildId: 2, channelId: 20));

        // Assert
        firstGuildResult.Value.ShouldBeOfType<None>();
        var detection = secondGuildResult.Value.ShouldBeOfType<Some<ImageBurstDetection>>().Value;
        detection.GuildId.ShouldBe(2UL);
        detection.ChannelIds.ShouldBe([10UL, 20UL]);
    }

    [Test]
    public void Observe_WithThreeQualifyingMessagesAcrossThreeChannelsInsideFifteenSeconds_ReturnsDetection()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(BaseTimestamp);

        ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild = new()
        {
            [1] = new ImageBurstSpamDetectorOptions
            {
                Window = 15,
                DistinctChannelThreshold = 3,
                RequiredAttachmentCount = 2,
                HandlingLockDuration = TimeSpan.FromMinutes(5),
            },
        };

        var detector = new ImageBurstSpamDetector(timeProvider, settingsByGuild);

        detector.Observe(CreateMessage(channelId: 908076393198419988, timestamp: BaseTimestamp));
        detector.Observe(CreateMessage(channelId: 903481102591750184, timestamp: BaseTimestamp.AddMilliseconds(2161)));

        // Act
        Option<ImageBurstDetection> result = detector.Observe(
            CreateMessage(channelId: 486841085210132490, timestamp: BaseTimestamp.AddMilliseconds(4362)));

        // Assert
        var detection = result.Value.ShouldBeOfType<Some<ImageBurstDetection>>().Value;
        detection.ChannelIds.ShouldBe([486841085210132490UL, 903481102591750184UL, 908076393198419988UL]);
        detection.Messages.Count.ShouldBe(3);
    }

    private sealed class TestTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return timestamp;
        }
    }
}