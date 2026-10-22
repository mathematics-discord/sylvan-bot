namespace RatBot.Features.EmojiYoink;

public static class EmojiName
{
    public static string Normalize(string invokedSourceName)
    {
        var suffixStart = invokedSourceName.LastIndexOf('~');

        if (
            suffixStart >= 0
            && suffixStart < invokedSourceName.Length - 1
            && invokedSourceName[suffixStart + 1] is >= '1' and <= '9'
            && invokedSourceName[(suffixStart + 2)..].All(character => character is >= '0' and <= '9')
        )
            invokedSourceName = invokedSourceName[..suffixStart];

        var validName = invokedSourceName.Length is >= 2 and <= 32 && invokedSourceName.All(IsValidCharacter);

        return !validName
            ? throw new ArgumentException(EmojiYoinkMessages.InvalidDestinationName, nameof(invokedSourceName))
            : invokedSourceName;
    }

    private static bool IsValidCharacter(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
    }
}