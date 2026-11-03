using SylvanBot.Application.Features.Automod;
using SylvanBot.Features.Automod;

namespace SylvanBot.Tests.Features.Automod;

public sealed class AutomodRuleListTests
{
    [TestCase(20, 1)]
    [TestCase(21, 2)]
    [TestCase(40, 2)]
    [TestCase(41, 3)]
    public void CreatePage_ShortRules_ShowsAtMostTwentyEntries(int count, int expectedPages)
    {
        // Arrange
        AutomodRule[] rules = Enumerable.Range(1, count)
            .Select(id => new AutomodRule { Id = id, Pattern = "spaghetti", Action = AutomodAction.Delete })
            .ToArray();

        // Act
        var first = AutomodRuleList.CreatePage(rules, 0);
        var last = AutomodRuleList.CreatePage(rules, int.MaxValue);

        // Assert
        first.Count.ShouldBe(expectedPages);
        first.Text.Split('\n').Count(line => line.StartsWith("- #", StringComparison.Ordinal)).ShouldBe(20);
        last.Index.ShouldBe(expectedPages - 1);
        last.Text.ShouldContain($"- #{count} ? Word ? Delete ? ||`spaghetti`||");
    }

    [Test]
    public void CreatePage_UnorderedRules_SortsIdsAndFormatsSpoileredPatterns()
    {
        // Arrange
        AutomodRule[] rules =
        [
            new AutomodRule
                { Id = 19, Pattern = "spag+h?etti", MatchType = AutomodMatchType.Regex, Action = AutomodAction.Mute, DurationSeconds = 1800 },
            new AutomodRule { Id = 12, Pattern = "spaghetti", Action = AutomodAction.Delete },
        ];

        // Act
        var page = AutomodRuleList.CreatePage(rules, 0);

        // Assert
        page.Text.ShouldBe(
            "### Automod rules\nPage 1 of 1\n\n- #12 ? Word ? Delete ? ||`spaghetti`||\n- #19 ? Regex ? Mute, 30 minutes ? ||`spag+h?etti`||");
    }

    [Test]
    public void CreatePage_DelimitersAndLineBreaks_EncodesDisplayWithoutMutatingRule()
    {
        // Arrange
        const string pattern = "`a||b`\r\n\t\\d+@everyone";
        var rule = new AutomodRule { Id = 1, Pattern = pattern, MatchType = AutomodMatchType.Regex };

        // Act
        var page = AutomodRuleList.CreatePage([rule], 0);

        // Assert
        page.Text.ShouldContain("||`\\u0060a\\u007C\\u007Cb\\u0060\\r\\n\\t\\d+@everyone`||");
        rule.Pattern.ShouldBe(pattern);
    }

    [Test]
    public void CreatePage_LongEntries_ReducesEntryCountBeforeTextLimit()
    {
        // Arrange
        AutomodRule[] rules = Enumerable.Range(1, 3)
            .Select(id => new AutomodRule { Id = id, Pattern = new string('x', 2000) })
            .ToArray();

        // Act
        var first = AutomodRuleList.CreatePage(rules, 0);
        var second = AutomodRuleList.CreatePage(rules, 1);

        // Assert
        first.Count.ShouldBe(3);
        first.Text.ShouldContain("- #1");
        first.Text.ShouldNotContain("- #2");
        second.Text.ShouldContain("- #2");
        first.Text.Length.ShouldBeLessThan(4000);
        second.Text.Length.ShouldBeLessThan(4000);
    }

    [Test]
    public void CreatePage_OversizedPattern_ContinuesWithoutLosingCharacters()
    {
        // Arrange
        var rule = new AutomodRule { Id = 1, Pattern = new string('x', 10000) };
        var first = AutomodRuleList.CreatePage([rule], 0);
        var content = new System.Text.StringBuilder();

        // Act
        for (var index = 0; index < first.Count; index++)
        {
            var page = AutomodRuleList.CreatePage([rule], index);
            var start = page.Text.IndexOf("||`", StringComparison.Ordinal) + 3;
            var end = page.Text.IndexOf("`||", start, StringComparison.Ordinal);
            content.Append(page.Text[start..end]);

            // Assert
            page.Text.Length.ShouldBeLessThan(4000);

            if (index > 0)
                page.Text.ShouldContain("(continued)");
        }

        content.ToString().ShouldBe(rule.Pattern);
    }

    [Test]
    public void CreatePage_OversizedUnicodePattern_KeepsRunesAndEscapeSequencesIntact()
    {
        // Arrange
        var rule = new AutomodRule { Id = 1, Pattern = string.Concat(Enumerable.Repeat("??|", 2000)) };
        var first = AutomodRuleList.CreatePage([rule], 0);
        var content = new System.Text.StringBuilder();

        // Act
        for (var index = 0; index < first.Count; index++)
        {
            var page = AutomodRuleList.CreatePage([rule], index);
            var start = page.Text.IndexOf("||`", StringComparison.Ordinal) + 3;
            var end = page.Text.IndexOf("`||", start, StringComparison.Ordinal);
            var part = page.Text[start..end];
            content.Append(part);

            // Assert
            char.IsLowSurrogate(part[0]).ShouldBeFalse();
            char.IsHighSurrogate(part[^1]).ShouldBeFalse();
            page.Text.Length.ShouldBeLessThan(4000);
        }

        content.ToString().ShouldBe(string.Concat(Enumerable.Repeat("??\\u007C", 2000)));
    }

    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void CreatePage_EmptyRules_ClampsToSingleEmptyPage(int requestedPage)
    {
        // Act
        var page = AutomodRuleList.CreatePage([], requestedPage);

        // Assert
        page.Index.ShouldBe(0);
        page.Count.ShouldBe(1);
        page.Text.ShouldContain("No automod rules in this guild.");
    }

    [Test]
    public void CreatePage_StalePageAfterRemoval_ClampsToLastAvailablePage()
    {
        // Arrange
        List<AutomodRule> rules = Enumerable.Range(1, 21)
            .Select(id => new AutomodRule { Id = id, Pattern = "hello" })
            .ToList();
        rules.RemoveAt(20);

        // Act
        var page = AutomodRuleList.CreatePage(rules, 1);

        // Assert
        page.Index.ShouldBe(0);
        page.Count.ShouldBe(1);
        page.Text.ShouldContain("- #20");
    }
}