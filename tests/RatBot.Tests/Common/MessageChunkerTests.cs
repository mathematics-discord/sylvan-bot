namespace RatBot.Tests.Common;

using RatBot.Common;

public sealed class MessageChunkerTests
{
    [Test]
    public void SplitMessageIntoChunks_WithExactLimit_ReturnsOneChunk()
    {
        // Arrange
        var message = new string('a', 2000);

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message);

        // Assert
        chunks.ShouldBe([message]);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void SplitMessageIntoChunks_InvalidChunkSize_ThrowsArgumentOutOfRange(int chunkSize)
    {
        // Arrange

        // Act
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            MessageChunker.SplitMessageIntoChunks("message", chunkSize));

        // Assert
        exception.ParamName.ShouldBe("chunkSize");
        exception.ActualValue.ShouldBe(chunkSize);
    }

    [Test]
    public void SplitMessageIntoChunks_WithMessageOverLimit_ReturnsTwoChunks()
    {
        // Arrange
        var message = new string('a', 2001);

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message);

        // Assert
        chunks.Length.ShouldBe(2);
        chunks[0].Length.ShouldBe(2000);
        chunks[1].ShouldBe("a");
    }

    [Test]
    public void SplitMessageIntoChunks_WithNewlineBeforeLimit_SplitsAtNewline()
    {
        // Arrange
        var firstLine = new string('a', 10);
        var secondLine = new string('b', 10);
        var message = $"{firstLine}\n{secondLine}";

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message, 12);

        // Assert
        chunks.ShouldBe([$"{firstLine}\n", secondLine]);
    }

    [Test]
    public void SplitMessageIntoChunks_WithNoNewlineBeforeLimit_SplitsAtLimit()
    {
        // Arrange
        var message = "abcdef";

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message, 3);

        // Assert
        chunks.ShouldBe(["abc", "def"]);
    }
}