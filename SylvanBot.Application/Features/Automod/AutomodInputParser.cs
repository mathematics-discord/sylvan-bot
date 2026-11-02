using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Automod;

public static class AutomodInputParser
{
    public static ImmutableArray<AutomodPattern> Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var patterns = ImmutableArray.CreateBuilder<AutomodPattern>();
        var position = 0;

        while (true)
        {
            while (position < input.Length && char.IsWhiteSpace(input[position]))
                position++;

            if (position == input.Length)
                throw new FormatException("Automod patterns cannot be empty.");

            var matchType = AutomodMatchType.Word;
            string pattern;

            if (input[position] == '`')
            {
                matchType = AutomodMatchType.Regex;
                var delimiter = input.AsSpan(position).StartsWith("```", StringComparison.Ordinal) ? "```" : "`";
                var start = position + delimiter.Length;
                var end = input.IndexOf(delimiter, start, StringComparison.Ordinal);

                if (end < 0)
                    throw new FormatException("Automod code delimiters must be closed.");

                pattern = input[start..end];
                var openingLineEnd = pattern.IndexOf('\n');

                if (delimiter.Length == 3 && openingLineEnd >= 0)
                {
                    pattern = pattern[(openingLineEnd + 1)..];

                    if (pattern.EndsWith('\n'))
                        pattern = pattern[..^1].TrimEnd('\r');
                }

                position = end + delimiter.Length;

                while (position < input.Length && char.IsWhiteSpace(input[position]))
                    position++;

                if (position < input.Length && input[position] != ',')
                    throw new FormatException("Separate automod patterns with commas.");
            }
            else
            {
                var end = input.IndexOf(value: ',', position);

                if (end < 0)
                    end = input.Length;

                pattern = input[position..end].Trim();

                if (pattern.Contains('`'))
                    throw new FormatException("Code delimiters must surround the entire pattern.");

                position = end;
            }

            if (string.IsNullOrWhiteSpace(pattern))
                throw new FormatException("Automod patterns cannot be empty.");

            AutomodMatcher.CreateRegex(pattern, matchType);

            patterns.Add(new AutomodPattern(pattern, matchType));

            if (position == input.Length)
                return patterns.ToImmutable();

            position++;
        }
    }
}