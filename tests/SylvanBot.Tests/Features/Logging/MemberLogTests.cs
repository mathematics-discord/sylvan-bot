using DotNext;
using SylvanBot.Application.Features.Logging;
using SylvanBot.Features.Logging;

namespace SylvanBot.Tests.Features.Logging;

[TestFixture]
public sealed class MemberLogTests
{
    private static readonly DateTimeOffset EventTime = new DateTimeOffset(2026, 11, 21, 15, 7, 0, TimeSpan.FromHours(1));

    [TestCase(null, "new", false)]
    [TestCase("", "", false)]
    [TestCase("same", "same", false)]
    [TestCase("", "new", true)]
    [TestCase("old", "new", true)]
    [TestCase("old", "", true)]
    [TestCase("Old", "old", true)]
    public void NicknameChange_RequiresKnownDifferentValues(string? before, string after, bool expected)
    {
        Optional<string> previous = before is null ? default : new Optional<string>(before);
        var change = MemberLogEntry.NicknameChanged(1, 42, "alex", previous, after, EventTime);

        change.HasValue.ShouldBe(expected);

        if (!expected)
            return;

        change.Value.PreviousNickname.Value.ShouldBe(before);
        change.Value.Nickname.Value.ShouldBe(after);
        change.Value.OccurredAtUtc.ShouldBe(EventTime);
    }

    [TestCase(MemberLogEvent.Join, "", "Member join", "unset")]
    [TestCase(MemberLogEvent.Leave, null, "Member leave", "unavailable")]
    [TestCase(MemberLogEvent.Leave, "Alex", "Member leave", "\"Alex\"")]
    public void Format_PreservesIdentityNicknameAvailabilityAndAbsoluteTime(
        MemberLogEvent kind, string? nickname, string label, string expectedNickname)
    {
        Optional<string> capturedNickname = nickname is null ? default : new Optional<string>(nickname);
        var entry = new MemberLogEntry(1, 42, "alex", capturedNickname, EventTime, kind);

        MemberLogFormatter.Format(entry).ShouldBe(
            $"{label} | <@42> | ID=42 | username=\"alex\" | nickname={expectedNickname} | 2026-11-21 14:07:00 UTC");
    }

    [Test]
    public void Format_ClearedNickname_ShowsOldValueAndUnset()
    {
        var entry = MemberLogEntry.NicknameChanged(1, 42, "alex", new Optional<string>("Alex"), string.Empty, EventTime).Value;

        MemberLogFormatter.Format(entry).ShouldBe(
            "Nickname change | <@42> | ID=42 | username=\"alex\" | nickname=\"Alex\" → unset | 2026-11-21 14:07:00 UTC");
    }

    [Test]
    public void Format_UntrustedNames_CannotAddLinesMarkupOrMentions()
    {
        const string name = "**x**\r\n\t\u2028\u2029\u202e<@123> @everyone `|\\\"";
        var entry = new MemberLogEntry(1, 42, name, new Optional<string>(name), EventTime, MemberLogEvent.Join);
        var line = MemberLogFormatter.Format(entry);

        line.ShouldNotContain('\r');
        line.ShouldNotContain('\n');
        line.ShouldNotContain('\u2028');
        line.ShouldNotContain('\u2029');
        line.ShouldNotContain('\u202e');
        line.ShouldNotContain("<@123>");
        line.ShouldNotContain("@everyone");
        line.ShouldContain("\\*\\*x\\*\\*");
        line.ShouldContain("\\u000d\\u000a\\u0009\\u2028\\u2029\\u202e");
        line.ShouldContain("＜＠123＞ ＠everyone \\`\\|\\\\\\\"");
        line.ShouldContain("<@42> | ID=42");
    }
}