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
        AutomodRule rule = new AutomodRule { Id = 1, Pattern = "hello" };
        AutomodMatcher matcher = new AutomodMatcher([rule]);

        // Act
        AutomodMatchResult result = matcher.Match(text);

        // Assert
        result.Match.HasValue.ShouldBe(expected);
        result.TimedOutRuleIds.ShouldBeEmpty();
    }

    [Test]
    public void Match_WordWithRegexCharacters_TreatsThemLiterally()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "a.b" }]);

        // Act / Assert
        matcher.Match("A.B").Match.HasValue.ShouldBeTrue();
        matcher.Match("axb").Match.HasValue.ShouldBeFalse();
    }

    [Test]
    public void Match_MultipleRules_SelectsEarliestPositionThenLowestId()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher(
        [
            new AutomodRule { Id = 1, Pattern = "later" },
            new AutomodRule { Id = 9, Pattern = "first" },
            new AutomodRule { Id = 4, Pattern = "first" },
        ]);

        // Act
        AutomodMatchResult result = matcher.Match("first, later");

        // Assert
        result.Match.Value.Rule.Id.ShouldBe(4);
        result.Match.Value.Reason.ShouldStartWith("Automatic action: message matches pattern 4\n");
    }

    [Test]
    public void Match_IndependentRegexes_PreservesBackreferencesAndCaseMatching()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher(
        [
            new AutomodRule { Id = 1, Pattern = "(x)\\1", MatchType = AutomodMatchType.Regex },
            new AutomodRule { Id = 2, Pattern = "(ab)\\1", MatchType = AutomodMatchType.Regex },
        ]);

        // Act
        AutomodMatchResult result = matcher.Match("ABab");

        // Assert
        result.Match.Value.Rule.Id.ShouldBe(2);
    }

    [Test]
    public void Match_NoMatchingRule_ReturnsNoMatch()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello" }]);

        // Act / Assert
        matcher.Match("goodbye").Match.HasValue.ShouldBeFalse();
        new AutomodMatcher([]).Match("hello").Match.HasValue.ShouldBeFalse();
    }

    [Test]
    public void Match_Invite_UsesLegacyReasonText()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher([new AutomodRule { Id = 240, Pattern = @"discord\.gg/\w+", MatchType = AutomodMatchType.Regex }]);

        // Act
        AutomodMatchResult result = matcher.Match("discord.gg/MC57wTZPaN");

        // Assert
        result.Match.Value.Reason.ShouldBe("Automatic action: message matches pattern 240\n`discord.gg/MC57wTZPaN`");
    }

    [Test]
    public void Match_MultilineMessage_PreservesContextOnOneLine()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello" }]);

        // Act
        AutomodMatchResult result = matcher.Match("before\n\t hello  \r\nafter");

        // Assert
        result.Match.Value.Reason.ShouldBe("Automatic action: message matches pattern 1\n`before hello after`");
    }

    [TestCase(0)]
    [TestCase(500)]
    [TestCase(1000)]
    public void Match_LongMessage_RetainsContextAroundSelectedMatch(int position)
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello", MatchType = AutomodMatchType.Regex }]);
        var text = new string('x', position) + "hello" + new string('y', 1000 - position);

        // Act
        AutomodMatchResult result = matcher.Match(text);

        // Assert
        result.Match.Value.Reason.ShouldContain("hello");
        result.Match.Value.Reason.ShouldContain("...");
        result.Match.Value.Reason.Length.ShouldBeLessThanOrEqualTo(440);
    }

    [Test]
    public void Match_BacktickHeavyMessage_KeepsReasonBoundedAndCodeDelimited()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher([new AutomodRule { Id = 1, Pattern = "hello" }]);

        // Act
        AutomodMatchResult result = matcher.Match("hello " + new string('`', 380));

        // Assert
        var context = result.Match.Value.Reason.Split('\n')[1];
        context.ShouldStartWith("`` ");
        context.ShouldEndWith(" ``");
        context[2..^2].ShouldNotContain("``");
        result.Match.Value.Reason.Length.ShouldBeLessThanOrEqualTo(512);
    }

    [Test]
    public void Match_RuleTimesOut_ReportsIdAndContinuesMatching()
    {
        // Arrange
        AutomodMatcher matcher = new AutomodMatcher(
        [
            new AutomodRule { Id = 1, Pattern = "^(a+)+$", MatchType = AutomodMatchType.Regex },
            new AutomodRule { Id = 2, Pattern = "a", MatchType = AutomodMatchType.Regex },
        ]);

        var text = new string('a', 50000) + "!";

        // Act
        AutomodMatchResult result = matcher.Match(text);

        // Assert
        result.Match.Value.Rule.Id.ShouldBe(2);
        result.TimedOutRuleIds.ShouldBe(new long[] { 1 });
    }
}