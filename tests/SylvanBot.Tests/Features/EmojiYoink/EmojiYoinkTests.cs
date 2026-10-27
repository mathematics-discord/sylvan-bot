using SylvanBot.Features.EmojiYoink;

namespace SylvanBot.Tests.Features.EmojiYoink;

public sealed class EmojiYoinkTests
{
    [TestCase("rat", "rat")]
    [TestCase("rat~1", "rat")]
    [TestCase("rat~27", "rat")]
    public void EmojiName_normalises_terminal_disambiguation_suffix(string invokedName, string expected)
    {
        ErrorOr<string> result = EmojiName.Normalize(invokedName);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(expected);
    }
}