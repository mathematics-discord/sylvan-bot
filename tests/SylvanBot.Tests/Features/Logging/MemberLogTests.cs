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
    public void NameChange_RequiresKnownDifferentValues(string? before, string after, bool expected)
    {
        Optional<string> previous = before is null ? default : new Optional<string>(before);

        foreach (MemberLogEvent kind in new[] { MemberLogEvent.NicknameChange, MemberLogEvent.DisplayNameChange, MemberLogEvent.UsernameChange })
        {
            Optional<MemberLogEntry> change = MemberLogEntry.NameChanged(1, 42, "alex", previous, after, EventTime, kind);
            change.HasValue.ShouldBe(expected);

            if (!expected)
                continue;

            change.Value.PreviousName.Value.ShouldBe(before);
            change.Value.Name.Value.ShouldBe(after);
            change.Value.Event.ShouldBe(kind);
            change.Value.Username.ShouldBe("alex");
            change.Value.GuildId.ShouldBe(1UL);
            change.Value.OccurredAtUtc.ShouldBe(EventTime);
        }
    }

    [TestCase(MemberLogEvent.Join, "Alex", "alex", "**Member Join** [20261121T14:07:00] - <@42> (`42`, `alex`)")]
    [TestCase(MemberLogEvent.Leave, null, "alex", "**Member Leave** [20261121T14:07:00] - <@42> (`42`, `alex`)")]
    [TestCase(MemberLogEvent.Leave, "Alex", "alex", "**Member Leave** [20261121T14:07:00] - <@42> (`42`, `alex`)")]
    public void Format_MembershipEvents_UseCompactIdentityAndUtcTime(
        MemberLogEvent kind,
        string? nickname,
        string username,
        string expectedLine)
    {
        Optional<string> capturedNickname = nickname is null ? default : new Optional<string>(nickname);
        MemberLogEntry entry = new MemberLogEntry(1, 42, username, capturedNickname, EventTime, kind);

        MemberLogFormatter.Format(entry).ShouldBe(expectedLine);
    }

    [Test]
    public void Format_ClearedNickname_ShowsOldValueAndUnset()
    {
        MemberLogEntry entry = MemberLogEntry.NameChanged(
                1,
                42,
                "alex",
                new Optional<string>("Alex"),
                string.Empty,
                EventTime,
                MemberLogEvent.NicknameChange)
            .Value;

        MemberLogFormatter.Format(entry)
            .ShouldBe(
                "**Nickname Change** [20261121T14:07:00] - <@42> (`42`, `alex`): `Alex` \u2192 unset");
    }

    [TestCase(MemberLogEvent.NicknameChange, "Nickname Change")]
    [TestCase(MemberLogEvent.DisplayNameChange, "Display Name Change")]
    [TestCase(MemberLogEvent.UsernameChange, "Username Change")]
    public void Format_NameChanges_ShowOldAndNewValues(MemberLogEvent kind, string label)
    {
        MemberLogEntry entry = MemberLogEntry.NameChanged(
                1,
                42,
                "new.account",
                new Optional<string>("Old Name"),
                "New Name",
                EventTime,
                kind)
            .Value;

        MemberLogFormatter.Format(entry)
            .ShouldBe(
                $"**{label}** [20261121T14:07:00] - <@42> (`42`, `new.account`): `Old Name` \u2192 `New Name`");
    }

    [TestCase("", "New Name", "unset \u2192 `New Name`")]
    [TestCase("Old Name", "", "`Old Name` \u2192 unset")]
    public void Format_DisplayNameCanBeSetOrCleared(string before, string after, string expectedChange)
    {
        MemberLogEntry entry = MemberLogEntry.NameChanged(
                1,
                42,
                "alex",
                new Optional<string>(before),
                after,
                EventTime,
                MemberLogEvent.DisplayNameChange)
            .Value;

        MemberLogFormatter.Format(entry)
            .ShouldBe(
                $"**Display Name Change** [20261121T14:07:00] - <@42> (`42`, `alex`): {expectedChange}");
    }

    [Test]
    public void Format_UntrustedNames_CannotAddLinesMarkupOrMentions()
    {
        const string name = "**x**\r\n\t\u2028\u2029\u202e<@123> @everyone `|\\\"";

        MemberLogEntry entry = MemberLogEntry.NameChanged(
                1,
                42,
                name,
                new Optional<string>("before"),
                name,
                EventTime,
                MemberLogEvent.DisplayNameChange)
            .Value;

        var line = MemberLogFormatter.Format(entry);

        line.ShouldNotContain('\r');
        line.ShouldNotContain('\n');
        line.ShouldNotContain('\u2028');
        line.ShouldNotContain('\u2029');
        line.ShouldNotContain('\u202e');
        line.ShouldNotContain("<@123>");
        line.ShouldNotContain("@everyone");
        line.ShouldContain(@"\u000d\u000a\u0009\u2028\u2029\u202e");
        line.ShouldContain("\uff1c\uff20123\uff1e \uff20everyone \\u0060");
        line.ShouldContain("<@42> (`42`, ");
        line.Count(character => character == '`').ShouldBe(8);
    }
}