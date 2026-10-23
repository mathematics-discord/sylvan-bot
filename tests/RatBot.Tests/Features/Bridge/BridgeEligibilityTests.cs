namespace RatBot.Tests.Features.Bridge;

using RatBot.Features.Bridge;

public sealed class BridgeEligibilityTests
{
    [TestCase("hello <@123>", true)]
    [TestCase("hello <@!123>", true)]
    [TestCase("reply notification only", false)]
    [TestCase("<@1234>", false)]
    [TestCase("<@&123>", false)]
    [TestCase("@Bridge", false)]
    public void IsEligible_StructuredMention_RequiresExactBodyToken(string content, bool expected)
    {
        // Arrange
        ulong[] mentions = [123];

        // Act
        var eligible = BridgeEligibility.IsEligible(true, true, true, 123, content, mentions);

        // Assert
        eligible.ShouldBe(expected);
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void IsEligible_DisallowedSourceOrChannel_RejectsContent(bool guild, bool human, bool configured)
    {
        // Act
        var eligible = BridgeEligibility.IsEligible(guild, human, configured, 123, "<@123> secret", [123]);

        // Assert
        eligible.ShouldBeFalse();
    }

    [Test]
    public void IsEligible_BodyTokenWithoutStructuredMention_RejectsMessage()
    {
        // Act
        var eligible = BridgeEligibility.IsEligible(true, true, true, 123, "<@123>", [456]);

        // Assert
        eligible.ShouldBeFalse();
    }
}