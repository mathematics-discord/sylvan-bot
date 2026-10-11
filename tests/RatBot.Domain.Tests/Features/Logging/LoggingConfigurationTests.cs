using RatBot.Domain.Common;
using RatBot.Domain.Features.Logging;
using Shouldly;

namespace RatBot.Domain.Tests.Features.Logging;

[TestFixture]
public sealed class LoggingConfigurationTests
{
    [TestCase(10UL, null, LoggingEventKind.Delete, 10UL)]
    [TestCase(10UL, null, LoggingEventKind.BulkDelete, 10UL)]
    [TestCase(10UL, null, LoggingEventKind.Edit, 10UL)]
    [TestCase(null, 20UL, LoggingEventKind.Delete, 20UL)]
    [TestCase(null, 20UL, LoggingEventKind.BulkDelete, 20UL)]
    [TestCase(null, 20UL, LoggingEventKind.Edit, 20UL)]
    [TestCase(10UL, 20UL, LoggingEventKind.Delete, 10UL)]
    [TestCase(10UL, 20UL, LoggingEventKind.BulkDelete, 10UL)]
    [TestCase(10UL, 20UL, LoggingEventKind.Edit, 20UL)]
    public void GetDestinationChannelId_ConfiguredChannels_ReturnsPreferredOrFallbackChannel(
        ulong? deleteChannelId, ulong? editChannelId, LoggingEventKind eventKind, ulong expectedChannelId)
    {
        // Arrange
        LoggingConfiguration configuration = new LoggingConfiguration(
            guildId: 1,
            enabled: true,
            deleteLogChannelId: deleteChannelId,
            editLogChannelId: editChannelId,
            evidenceRetentionPeriod: TimeSpan.FromMinutes(15));

        // Act
        Option<ulong> result = configuration.GetDestinationChannelId(eventKind);

        // Assert
        result.Value.ShouldBeOfType<Some<ulong>>().Value.ShouldBe(expectedChannelId);
    }

    [TestCase(LoggingEventKind.Delete)]
    [TestCase(LoggingEventKind.BulkDelete)]
    [TestCase(LoggingEventKind.Edit)]
    public void GetDestinationChannelId_NoChannelsConfigured_ReturnsNone(LoggingEventKind eventKind)
    {
        // Arrange
        LoggingConfiguration configuration = LoggingConfiguration.Disabled(1);

        // Act
        Option<ulong> result = configuration.GetDestinationChannelId(eventKind);

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public void AllowsLogging_WhenChannelIsExcluded_ReturnsFalseEvenWhenEnabled()
    {
        LoggingConfiguration configuration = new LoggingConfiguration(1, true, 10, null, TimeSpan.FromMinutes(15));

        configuration.AllowsLogging(true).ShouldBeFalse();
    }

    [Test]
    public void AllowsLogging_WhenConfigurationIsDisabled_ReturnsFalse()
    {
        LoggingConfiguration configuration = new LoggingConfiguration(1, false, 10, null, TimeSpan.FromMinutes(15));

        configuration.AllowsLogging(false).ShouldBeFalse();
    }
}