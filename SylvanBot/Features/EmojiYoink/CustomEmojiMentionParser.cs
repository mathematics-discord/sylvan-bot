using System.Text.RegularExpressions;

namespace SylvanBot.Features.EmojiYoink;

using static RegexOptions;

public static partial class CustomEmojiMentionParser
{
    [GeneratedRegex(@"<a?:[^<>\s:]+:[0-9]+>", options: Compiled, matchTimeoutMilliseconds: 500)]
    private static partial Regex MentionPattern { get; }

    public static Result<Emote> ParseStrict(string input)
    {
        var candidate = input.Trim();

        return ParseMention(candidate: candidate, error: EmojiYoinkErrors.InvalidCustomEmoji);
    }

    public static Result<Emote> ParseMessage(string content)
    {
        var matches = MentionPattern.Matches(content);

        if (matches.Count != 1)
            return new Result<Emote>(EmojiYoinkErrors.InvalidMessageEmoji);

        return ParseMention(candidate: matches[0].Value, error: EmojiYoinkErrors.InvalidMessageEmoji);
    }

    private static Result<Emote> ParseMention(string candidate, OperationFailureException error)
    {
        if (
            !Emote.TryParse(text: candidate, out var emote)
            || emote.Id == 0
            || !string.Equals(a: candidate, emote.ToString(), comparisonType: StringComparison.Ordinal)
        )
            return new Result<Emote>(error);

        return emote;
    }
}