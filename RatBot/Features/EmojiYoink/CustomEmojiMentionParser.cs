using System.Text.RegularExpressions;
using static System.Text.RegularExpressions.RegexOptions;

namespace RatBot.Features.EmojiYoink;

public static partial class CustomEmojiMentionParser
{
    [GeneratedRegex(@"<a?:[^<>\s:]+:[0-9]+>", Compiled, 500)]
    private static partial Regex MentionPattern { get; }

    public static Emote ParseStrict(string input)
    {
        var candidate = input.Trim();

        return ParseMention(candidate, EmojiYoinkMessages.InvalidCustomEmoji);
    }

    public static Emote ParseMessage(string content)
    {
        var matches = MentionPattern.Matches(content);

        if (matches.Count != 1)
            throw new FormatException(EmojiYoinkMessages.InvalidMessageEmoji);

        return ParseMention(matches[0].Value, EmojiYoinkMessages.InvalidMessageEmoji);
    }

    private static Emote ParseMention(string candidate, string message)
    {
        if (!Emote.TryParse(candidate, out var emote)
            || emote.Id == 0
            || !string.Equals(candidate, emote.ToString(), StringComparison.Ordinal))
            throw new FormatException(message);

        return emote;
    }
}