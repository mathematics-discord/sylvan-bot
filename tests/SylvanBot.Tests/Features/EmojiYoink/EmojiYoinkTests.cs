using SylvanBot.Features.EmojiYoink;

namespace SylvanBot.Tests.Features.EmojiYoink;

public sealed class EmojiYoinkTests
{
    [TestCase("rat", "rat")]
    [TestCase("rat~1", "rat")]
    [TestCase("rat~27", "rat")]
    public void EmojiName_normalises_terminal_disambiguation_suffix(string invokedName, string expected)
    {
        Result<string> result = EmojiName.Normalize(invokedName);

        result.IsSuccessful.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Test]
    public void Normalize_InvalidName_ReturnsDescriptiveValidationFailure()
    {
        // Arrange
        const string name = "!";

        // Act
        Result<string> result = EmojiName.Normalize(name);

        // Assert
        result.IsSuccessful.ShouldBeFalse();
        var failure = result.Error.ShouldBeOfType<OperationFailureException>();
        failure.Category.ShouldBe(OperationFailureCategory.Validation);
        failure.Code.ShouldBe("EmojiYoink.InvalidDestinationName");

        failure.Message.ShouldBe(
            "The emoji name must become 2–32 ASCII letters, digits, or underscores after removing a Discord disambiguation suffix.");
    }
}