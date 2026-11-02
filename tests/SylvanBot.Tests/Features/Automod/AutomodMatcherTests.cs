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
        new AutomodMatcher([]).Match("hello").Rule.HasValue.ShouldBeFalse();
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