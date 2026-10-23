using System.Collections.Immutable;

namespace RatBot.Tests.Features.Bridge;

using RatBot.Features.Bridge;

public sealed class BridgeTextTests
{
    [TestCase(1)]
    [TestCase(1996)]
    [TestCase(2000)]
    [TestCase(16000)]
    public void Split_UnbrokenText_PreservesContentWithinDiscordLimit(int length)
    {
        // Arrange
        var text = new string('a', length);

        // Act
        ImmutableArray<string> chunks = BridgeText.Split(text);

        // Assert
        string.Concat(chunks).ShouldBe(text);
        chunks.ShouldAllBe(chunk => chunk.Length > 0 && chunk.Length <= 2000);
    }

    [Test]
    public void Split_Paragraphs_PrefersLineBoundaries()
    {
        // Arrange
        var first = new string('a', 1200) + "\n";
        var second = new string('b', 1200);

        // Act
        ImmutableArray<string> chunks = BridgeText.Split(first + second);

        // Assert
        chunks.ShouldBe([first, second]);
    }

    [Test]
    public void Split_CodeBlock_ClosesAndReopensFence()
    {
        // Arrange
        var text = "```csharp\n" + new string('x', 4500) + "\n```";

        // Act
        ImmutableArray<string> chunks = BridgeText.Split(text);

        // Assert
        chunks.Length.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(chunk => chunk.Length <= 2000);
        chunks.ShouldAllBe(chunk => chunk.StartsWith("```csharp\n", StringComparison.Ordinal));
        chunks.ShouldAllBe(chunk => chunk.EndsWith("```", StringComparison.Ordinal));
        string.Concat(chunks).Count(character => character == 'x').ShouldBe(4500);
    }

    [Test]
    public void Split_LeadingBlankChunks_OmitsUnsendableChunks()
    {
        // Arrange
        var text = new string(' ', 4000) + "Hello";

        // Act
        ImmutableArray<string> chunks = BridgeText.Split(text);

        // Assert
        chunks.ShouldAllBe(chunk => !string.IsNullOrWhiteSpace(chunk));
        string.Concat(chunks).TrimStart().ShouldBe("Hello");
    }

    [Test]
    public void Split_EmojiAtBoundary_DoesNotSeparateSurrogatePair()
    {
        // Arrange
        var text = new string('a', 1995) + "🐀" + new string('b', 100);

        // Act
        ImmutableArray<string> chunks = BridgeText.Split(text);

        // Assert
        string.Concat(chunks).ShouldBe(text);
        chunks.ShouldAllBe(chunk => !char.IsHighSurrogate(chunk[chunk.Length - 1]) && !char.IsLowSurrogate(chunk[0]));
    }
}
