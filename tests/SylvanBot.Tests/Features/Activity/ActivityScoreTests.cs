using SylvanBot.Features.Activity;

namespace SylvanBot.Tests.Features.Activity;

public sealed class ActivityScoreTests
{
    [TestCase("0", "2", "3", "0")]
    [TestCase("1", "2", "3", "1")]
    [TestCase("1.5", "2", "3", "1.5")]
    [TestCase(null, "2", "3", "2")]
    [TestCase(null, "0", "3", "0")]
    [TestCase(null, null, "3", "3")]
    [TestCase(null, null, null, "1")]
    public void ResolveWeight_ThreadOverrides_UseMostSpecificWeight(decimal? threadWeight, decimal? channelWeight, decimal? categoryWeight, decimal expected)
    {
        // Arrange
        Dictionary<ulong, decimal> weights = new Dictionary<ulong, decimal>();

        if (threadWeight.HasValue)
            weights[10] = threadWeight.Value;

        if (channelWeight.HasValue)
            weights[20] = channelWeight.Value;

        if (categoryWeight.HasValue)
            weights[30] = categoryWeight.Value;

        // Act
        var actual = ActivityService.ResolveWeight(weights, channelId: 10, parentId: 20, categoryId: 30);

        // Assert
        actual.ShouldBe(expected);
    }

    [Test]
    public void ResolveWeight_RemovedThreadOverride_RestoresParentWeight()
    {
        // Arrange
        Dictionary<ulong, decimal> weights = new Dictionary<ulong, decimal> { [10] = 0m, [20] = 2m };
        weights.Remove(10);

        // Act
        var actual = ActivityService.ResolveWeight(weights, channelId: 10, parentId: 20, categoryId: null);

        // Assert
        actual.ShouldBe(2m);
    }

    [Test]
    public void ResolveWeight_UncategorisedChannelWithoutOverride_UsesDefault()
    {
        // Arrange
        Dictionary<ulong, decimal> weights = new Dictionary<ulong, decimal>();

        // Act
        var actual = ActivityService.ResolveWeight(weights, channelId: 20, parentId: 20, categoryId: null);

        // Assert
        actual.ShouldBe(1m);
    }

    [TestCase("1.4", 0, 0, "1.4")]
    [TestCase("1.4", "99.5", 100, "0.5")]
    [TestCase(1, 100, 100, 0)]
    [TestCase(1, 101, 100, 0)]
    [TestCase(0, 0, 100, 0)]
    public void Award_RemainingAllowance_PreservesFractionsAndHonoursCap(decimal weight, decimal recent, long cap, decimal expected)
    {
        // Act
        var actual = ActivityService.Award(weight, recent, cap);

        // Assert
        actual.ShouldBe(expected);
    }
}
