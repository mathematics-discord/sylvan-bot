namespace RatBot.Features.EmojiYoink;

using System.Text.RegularExpressions;
using static System.Text.RegularExpressions.RegexOptions;

public static partial class CustomEmojiMentionParser
{
    [GeneratedRegex(@"<a?:[^<>\s:]+:[0-9]+>", Compiled, 500)]
    private static partial Regex MentionPattern { get; }

    public static ErrorOr<Emote> ParseStrict(string input)
    {
        var candidate = input.Trim();

        return ParseMention(candidate, EmojiYoinkErrors.InvalidCustomEmoji);
    }

    public static ErrorOr<Emote> ParseMessage(string content)
    {
        var matches = MentionPattern.Matches(content);

        if (matches.Count != 1)
            return EmojiYoinkErrors.InvalidMessageEmoji;

        return ParseMention(matches[0].Value, EmojiYoinkErrors.InvalidMessageEmoji);
    }

    private static ErrorOr<Emote> ParseMention(string candidate, Error error)
    {
        if (!Emote.TryParse(candidate, out var emote)
            || emote.Id == 0
            || !string.Equals(candidate, emote.ToString(), StringComparison.Ordinal))
            return error;

        return emote;
    }
}