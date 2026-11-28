using System.Text.RegularExpressions;

namespace SylvanBot.Features.EmojiYoink;

using static RegexOptions;

public static partial class CustomEmojiMentionParser
{
    [GeneratedRegex(@"<a?:[^<>\s:]+:[0-9]+>", options: Compiled, matchTimeoutMilliseconds: 500)]
    private static partial Regex MentionPattern { get; }

    public static Emote ParseStrict(string input)
    {
        var candidate = input.Trim();

        return ParseMention(candidate: candidate, error: EmojiYoinkErrors.InvalidCustomEmoji);
    }

    public static Emote ParseMessage(string content)
    {
        MatchCollection matches = MentionPattern.Matches(content);

        if (matches.Count != 1)
            throw EmojiYoinkErrors.InvalidMessageEmoji;

        return ParseMention(candidate: matches[0].Value, error: EmojiYoinkErrors.InvalidMessageEmoji);
    }

    private static Emote ParseMention(string candidate, EmojiImportException error)
    {
        if (
            !Emote.TryParse(text: candidate, out Emote emote)
            || emote.Id == 0
            || !string.Equals(a: candidate, emote.ToString(), comparisonType: StringComparison.Ordinal)
        )

            throw error;


        return emote;
    }
}