using SylvanBot.Application.Features.Automod;
using SylvanBot.Features.Automod;

namespace SylvanBot.Tests.Features.Automod;

public sealed class AutomodReplyTests
{
    [Test]
    public void FormatRule_DisabledTimedRegex_ShowsStoredPatternAndPunishment()
    {
        const string pattern = @"offer\s+\d+|`sale`|@everyone";
        AutomodRule rule = new AutomodRule
        {
            Id = 12,
            Disabled = true,
            MatchType = AutomodMatchType.Regex,
            Pattern = pattern,
            Action = AutomodAction.Mute,
            DurationSeconds = 60,
        };

        var (text, attachPattern) = AutomodReply.FormatRule(rule);

        attachPattern.ShouldBeFalse();
        text.ShouldBe($"**Automod rule #12**\nDisabled | Regex | Mute, 1 minute\n```\n{pattern}\n```");
    }

    [TestCase("a```b")]
    [TestCase("a\rb")]
    public void FormatRule_PatternUnsafeForCodeBlock_UsesAttachment(string pattern)
    {
        AutomodRule rule = new AutomodRule { Id = 2, Pattern = pattern, Action = AutomodAction.Ban };

        var (text, attachPattern) = AutomodReply.FormatRule(rule);

        attachPattern.ShouldBeTrue();
        text.ShouldBe("**Automod rule #2**\nActive | Word | Ban, permanent\nThe full pattern is attached.");
    }

    [Test]
    public void FormatRule_PatternAtMessageLengthLimit_ShowsFullPattern()
    {
        const string emptyContent = "**Automod rule #2**\nActive | Word | Delete\n```\n\n```";
        var pattern = new string('a', 2000 - emptyContent.Length);

        var (text, attachPattern) = AutomodReply.FormatRule(new AutomodRule { Id = 2, Pattern = pattern });

        attachPattern.ShouldBeFalse();
        text.Length.ShouldBe(2000);
        text.ShouldContain(pattern);
        AutomodReply.FormatRule(new AutomodRule { Id = 2, Pattern = pattern + "a" }).AttachPattern.ShouldBeTrue();
    }
}
