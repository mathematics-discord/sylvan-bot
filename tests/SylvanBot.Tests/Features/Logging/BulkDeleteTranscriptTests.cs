using System.Text;
using SylvanBot.Features.Logging;

namespace SylvanBot.Tests.Features.Logging;

[TestFixture]
public sealed class BulkDeleteTranscriptTests
{
    [Test]
    public void Create_Message_UsesUtcTimeAndChatLogFormat()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2026, 11, 6, 14, 34, 56, TimeSpan.FromHours(2));

        // Act
        var text = Encoding.UTF8.GetString(BulkDeleteTranscript.Create([(timestamp, "general", "ratgirl", "hello 🐀 café")]));

        // Assert
        text.ShouldBe("[12:34:56] #general ratgirl: hello 🐀 café\n");
    }

    [TestCase("\r\n")]
    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\u2028")]
    [TestCase("\u2029")]
    public void Create_MultilineContent_PrefixesEveryLine(string newline)
    {
        // Arrange
        var content = $"  first{newline}{newline}\t\"quoted\" \\ backslash 🐀 café  {newline}";

        // Act
        var text = Encoding.UTF8.GetString(BulkDeleteTranscript.Create([(DateTimeOffset.UnixEpoch, "general", "alice", content)]));

        // Assert
        text.ShouldBe(
            "[00:00:00] #general alice:   first\n"
            + "[00:00:00] #general alice: \n"
            + "[00:00:00] #general alice: \t\"quoted\" \\ backslash 🐀 café  \n"
            + "[00:00:00] #general alice: \n");
    }

    [Test]
    public void Create_EmptyContent_StillIncludesMessageHeader()
    {
        // Act
        var text = Encoding.UTF8.GetString(BulkDeleteTranscript.Create([(DateTimeOffset.UnixEpoch, "general", "alice", "")]));

        // Assert
        text.ShouldBe("[00:00:00] #general alice: \n");
    }

    [Test]
    public void Create_NewlinesInLabels_CannotIntroduceUnprefixedLines()
    {
        // Act
        var text = Encoding.UTF8.GetString(BulkDeleteTranscript.Create([(DateTimeOffset.UnixEpoch, "general\nchat", "alice\r\nbob", "hello")]));

        // Assert
        text.ShouldBe("[00:00:00] #general chat alice bob: hello\n");
    }

    [Test]
    public void Create_ManyLongMessages_PreservesEveryRecordAndFullContent()
    {
        // Arrange
        var content = new string('x', 4000);

        (DateTimeOffset, string, string, string content)[] messages = Enumerable
           .Range(1, 100)
           .Select(index => (DateTimeOffset.UnixEpoch.AddSeconds(index), "general", $"user{index}", content))
           .ToArray();

        // Act
        var text = Encoding.UTF8.GetString(BulkDeleteTranscript.Create(messages));

        // Assert
        var lines = text.Split('\n');
        lines.Length.ShouldBe(101);
        lines[^1].ShouldBeEmpty();

        for (var index = 0; index < messages.Length; index++)
            lines[index].ShouldBe($"[00:{(index + 1) / 60:00}:{(index + 1) % 60:00}] #general user{index + 1}: {content}");
    }
}