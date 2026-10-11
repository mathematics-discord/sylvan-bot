using RatBot.Domain.Features.Logging;
using Shouldly;

namespace RatBot.Domain.Tests.Features.Logging;

[TestFixture]
public sealed class LoggingConfigurationTests
{
    [Test]
    public void GetDestinationChannelId_WhenOnlyDeleteChannelConfigured_RoutesAllEventsThere()
    {
        LoggingConfiguration configuration = new LoggingConfiguration(1, true, 10, null, TimeSpan.FromMinutes(15));

        configuration.GetDestinationChannelId(LoggingEventKind.Delete).ShouldBe(10UL);
        configuration.GetDestinationChannelId(LoggingEventKind.BulkDelete).ShouldBe(10UL);
        configuration.GetDestinationChannelId(LoggingEventKind.Edit).ShouldBe(10UL);
    }

    [Test]
    public void GetDestinationChannelId_WhenOnlyEditChannelConfigured_RoutesAllEventsThere()
    {
        LoggingConfiguration configuration = new LoggingConfiguration(1, true, null, 20, TimeSpan.FromMinutes(15));

        configuration.GetDestinationChannelId(LoggingEventKind.Delete).ShouldBe(20UL);
        configuration.GetDestinationChannelId(LoggingEventKind.BulkDelete).ShouldBe(20UL);
        configuration.GetDestinationChannelId(LoggingEventKind.Edit).ShouldBe(20UL);
    }

    [Test]
    public void GetDestinationChannelId_WhenBothChannelsConfigured_RoutesEditsSeparatelyFromDeletes()
    {
        LoggingConfiguration configuration = new LoggingConfiguration(1, true, 10, 20, TimeSpan.FromMinutes(15));

        configuration.GetDestinationChannelId(LoggingEventKind.Delete).ShouldBe(10UL);
        configuration.GetDestinationChannelId(LoggingEventKind.BulkDelete).ShouldBe(10UL);
        configuration.GetDestinationChannelId(LoggingEventKind.Edit).ShouldBe(20UL);
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