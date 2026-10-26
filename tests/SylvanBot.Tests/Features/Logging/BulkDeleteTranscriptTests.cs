namespace SylvanBot.Tests.Features.Logging;

using System.Text;
using System.Text.Json;
using SylvanBot.Features.Logging;

[TestFixture]
public sealed class BulkDeleteTranscriptTests
{
    [Test]
    public void Create_LargeIds_ExportsOnlyThreeFieldsWithExactStringIds()
    {
        // Arrange
        (ulong AuthorId, ulong ChannelId, string Content)[] messages = [(ulong.MaxValue, 268882317391429632, "hello")];

        // Act
        var bytes = BulkDeleteTranscript.Create(messages);

        // Assert
        using var document = JsonDocument.Parse(bytes);
        var row = document.RootElement;

        var propertyCount = row.EnumerateObject()
            .Count();

        propertyCount.ShouldBe(3);
        row.GetProperty("author_id").GetString().ShouldBe("18446744073709551615");
        row.GetProperty("channel_id").GetString().ShouldBe("268882317391429632");
        row.GetProperty("content").GetString().ShouldBe("hello");
        bytes[^1].ShouldBe((byte)'\n');
    }

    [TestCase("  first\r\nsecond\n\t\"quoted\" \\ backslash 🐀 café\u2028last  ")]
    [TestCase("")]
    public void Create_SpecialOrEmptyContent_PreservesContentOnOnePhysicalLine(string content)
    {
        // Arrange
        (ulong AuthorId, ulong ChannelId, string Content)[] messages = [(1, 2, content)];

        // Act
        var text = Encoding.UTF8.GetString(BulkDeleteTranscript.Create(messages));

        // Assert
        text.Split('\n').Length.ShouldBe(2);

        using var document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("content").GetString().ShouldBe(content);
    }

    [Test]
    public void Create_ManyLongMessages_PreservesEveryRecordAndFullContent()
    {
        // Arrange
        var content = new string('x', 4000);

        (ulong AuthorId, ulong ChannelId, string Content)[] messages = Enumerable.Range(1, 100)
            .Select(index => (AuthorId: (ulong)index, ChannelId: 268882317391429632UL, Content: content))
            .ToArray();

        // Act
        var text = Encoding.UTF8.GetString(BulkDeleteTranscript.Create(messages));

        // Assert
        var lines = text.Split('\n');
        lines.Length.ShouldBe(101);
        lines[^1].ShouldBeEmpty();

        for (var index = 0; index < messages.Length; index++)
        {
            using var document = JsonDocument.Parse(lines[index]);
            var row = document.RootElement;
            row.GetProperty("author_id").GetString().ShouldBe((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            row.GetProperty("content").GetString().ShouldBe(content);
        }
    }
}