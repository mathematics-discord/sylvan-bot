using SylvanBot.Application.Features.Automod;

namespace SylvanBot.Tests.Features.Automod;

public sealed class AutomodMatcherTests
{
    [TestCase("HELLO!", true)]
    [TestCase("say hello there", true)]
    [TestCase("helloworld", false)]
    [TestCase("ahello", false)]
    public void Match_Word_UsesBoundariesAndIgnoresCase(string text, bool expected)
    {
        // Arrange
        var rule = new AutomodRule { Id = 1, Pattern = "hello" };
        var matcher = new AutomodMatcher([rule]);

        // Act
        var result = matcher.Match(text);

        // Assert
        result.Rule.HasValue.ShouldBe(expected);
        result.TimedOutRuleIds.ShouldBeEmpty();
    }

    [Test]
    public void Match_WordWithRegexCharacters_TreatsThemLiterally()
    {
        // Arrange
        var matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "a.b" }]);

        // Act / Assert
        matcher.Match("A.B").Rule.HasValue.ShouldBeTrue();
        matcher.Match("axb").Rule.HasValue.ShouldBeFalse();
    }

    [Test]
    public void Match_MultipleRules_SelectsEarliestPositionThenLowestId()
    {
        // Arrange
        var matcher = new AutomodMatcher([
            new AutomodRule { Id = 1, Pattern = "later" },
            new AutomodRule { Id = 9, Pattern = "first" },
            new AutomodRule { Id = 4, Pattern = "first" },
        ]);

        // Act
        var result = matcher.Match("first, later");

        // Assert
        result.Rule.Value.Id.ShouldBe(4);
        result.Reason.Value.ShouldStartWith("Automatic action: message matches pattern 4\n");
    }

    [Test]
    public void Match_IndependentRegexes_PreservesBackreferencesAndCaseMatching()
    {
        // Arrange
        var matcher = new AutomodMatcher([
            new AutomodRule { Id = 1, Pattern = "(x)\\1", MatchType = AutomodMatchType.Regex },
            new AutomodRule { Id = 2, Pattern = "(ab)\\1", MatchType = AutomodMatchType.Regex },
        ]);

        // Act
        var result = matcher.Match("ABab");

        // Assert
        result.Rule.Value.Id.ShouldBe(2);
    }

    [Test]
    public void Match_NoMatchingRule_ReturnsNoMatch()
    {
        // Arrange
        var matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello" }]);

        // Act / Assert
        matcher.Match("goodbye").Rule.HasValue.ShouldBeFalse();
        matcher.Match("goodbye").Reason.HasValue.ShouldBeFalse();
        new AutomodMatcher([]).Match("hello").Rule.HasValue.ShouldBeFalse();
    }

    [Test]
    public void Match_Invite_UsesLegacyReasonText()
    {
        // Arrange
        var matcher = new AutomodMatcher([new AutomodRule { Id = 240, Pattern = @"discord\.gg/\w+", MatchType = AutomodMatchType.Regex }]);

        // Act
        var result = matcher.Match("discord.gg/MC57wTZPaN");

        // Assert
        result.Reason.Value.ShouldBe("Automatic action: message matches pattern 240\n`discord.gg/MC57wTZPaN`");
    }

    [Test]
    public void Match_MultilineMessage_PreservesContextOnOneLine()
    {
        // Arrange
        var matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello" }]);

        // Act
        var result = matcher.Match("before\n\t hello  \r\nafter");

        // Assert
        result.Reason.Value.ShouldBe("Automatic action: message matches pattern 1\n`before hello after`");
    }

    [TestCase(0)]
    [TestCase(500)]
    [TestCase(1000)]
    public void Match_LongMessage_RetainsContextAroundSelectedMatch(int position)
    {
        // Arrange
        var matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello", MatchType = AutomodMatchType.Regex }]);
        var text = new string('x', position) + "hello" + new string('y', 1000 - position);

        // Act
        var result = matcher.Match(text);

        // Assert
        result.Reason.Value.ShouldContain("hello");
        result.Reason.Value.ShouldContain("...");
        result.Reason.Value.Length.ShouldBeLessThanOrEqualTo(440);
    }

    [Test]
    public void Match_BacktickHeavyMessage_KeepsReasonBoundedAndCodeDelimited()
    {
        // Arrange
        var matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello" }]);

        // Act
        var result = matcher.Match("hello " + new string('`', 380));

        // Assert
        var context = result.Reason.Value.Split('\n')[1];
        context.ShouldStartWith("`` ");
        context.ShouldEndWith(" ``");
        context[2..^2].ShouldNotContain("``");
        result.Reason.Value.Length.ShouldBeLessThanOrEqualTo(512);
    }

    [Test]
    public void Match_RuleTimesOut_ReportsIdAndContinuesMatching()
    {
        // Arrange
        var matcher = new AutomodMatcher([
            new AutomodRule { Id = 1, Pattern = "^(a+)+$", MatchType = AutomodMatchType.Regex },
            new AutomodRule { Id = 2, Pattern = "a", MatchType = AutomodMatchType.Regex },
        ]);

        var text = new string('a', 50000) + "!";

        // Act
        var result = matcher.Match(text);

        // Assert
        result.Rule.Value.Id.ShouldBe(2);
        result.TimedOutRuleIds.ShouldBe(new long[] { 1 });
    }
}
