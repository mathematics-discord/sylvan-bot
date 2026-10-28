using SylvanBot.Common;

namespace SylvanBot.Tests.Common;

public sealed class MessageChunkerTests
{
    [Test]
    public void SplitMessageIntoChunks_WithExactLimit_ReturnsOneChunk()
    {
        // Arrange
        var message = new string(c: 'a', count: 2000);

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message).Value;

        // Assert
        chunks.ShouldBe([message]);
    }

    [Test]
    public void SplitMessageIntoChunks_WithInvalidChunkSize_ReturnsValidationError()
    {
        // Arrange

        // Act
        Result<string[]> result = MessageChunker.SplitMessageIntoChunks("message", chunkSize: 0);

        // Assert
        result.IsSuccessful.ShouldBeFalse();
        result.Error.ShouldBeOfType<OperationFailureException>().Category.ShouldBe(OperationFailureCategory.Validation);
    }

    [Test]
    public void SplitMessageIntoChunks_WithMessageOverLimit_ReturnsTwoChunks()
    {
        // Arrange
        var message = new string(c: 'a', count: 2001);

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message).Value;

        // Assert
        chunks.Length.ShouldBe(2);
        chunks[0].Length.ShouldBe(2000);
        chunks[1].ShouldBe("a");
    }

    [Test]
    public void SplitMessageIntoChunks_WithNewlineBeforeLimit_SplitsAtNewline()
    {
        // Arrange
        var firstLine = new string(c: 'a', count: 10);
        var secondLine = new string(c: 'b', count: 10);
        var message = $"{firstLine}\n{secondLine}";

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message: message, chunkSize: 12).Value;

        // Assert
        chunks.ShouldBe([$"{firstLine}\n", secondLine]);
    }

    [Test]
    public void SplitMessageIntoChunks_WithNoNewlineBeforeLimit_SplitsAtLimit()
    {
        // Arrange
        var message = "abcdef";

        // Act
        var chunks = MessageChunker.SplitMessageIntoChunks(message: message, chunkSize: 3).Value;

        // Assert
        chunks.ShouldBe(["abc", "def"]);
    }
}