using SylvanBot.Features.Activity;

namespace SylvanBot.Tests.Features.Activity;

public sealed class ActivityScoreTests
{
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
