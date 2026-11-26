using SylvanBot.Features.SealHunts;

namespace SylvanBot.Tests.Features.SealHunts;

public sealed class SealHuntDestinationTests
{
    [Test]
    public void EmptyWhitelist_ExcludesEveryDestination()
    {
        SealHuntWorker.IncludesDestination([], 10, 20, 30).ShouldBeFalse();
    }

    [TestCase(268882317391429632UL, true)]
    [TestCase(465988777798664211UL, false)]
    [TestCase(123UL, false)]
    public void SingleChannelWhitelist_ExcludesOtherChannels(ulong channelId, bool expected)
    {
        HashSet<long> allowed = [268882317391429632];

        SealHuntWorker.IncludesDestination(allowed, channelId, channelId, null).ShouldBe(expected);
    }

    [TestCase(10L, true)]
    [TestCase(20L, true)]
    [TestCase(30L, true)]
    [TestCase(40L, false)]
    public void DestinationRequiresAnExplicitChannelParentOrCategoryEntry(long destinationId, bool expected)
    {
        HashSet<long> allowed = [destinationId];

        SealHuntWorker.IncludesDestination(allowed, 10, 20, 30).ShouldBe(expected);
    }
}