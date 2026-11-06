using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace SylvanBot.Application.Features.Automod;

public sealed class AutomodMatcher
{
    private readonly ImmutableArray<(AutomodRule Rule, Regex Regex)> _rules;

    public AutomodMatcher(IEnumerable<AutomodRule> rules)
    {
        _rules = rules
            .Select(rule =>
            {
                var copy = new AutomodRule
                {
                    Id = rule.Id,
                    GuildId = rule.GuildId,
                    Pattern = rule.Pattern,
                    MatchType = rule.MatchType,
                    Action = rule.Action,
                    DurationSeconds = rule.DurationSeconds,
                };

                try
                {
                    return (copy, CreateRegex(copy.Pattern, copy.MatchType));
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException or RegexMatchTimeoutException)
                {
                    throw new FormatException($"Invalid automod rule {rule.Id} in guild {rule.GuildId}.", ex);
                }
            })
            .ToImmutableArray();
    }

    public AutomodMatchResult Match(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Optional<AutomodRule> selected = default;
        var earliestPosition = int.MaxValue;
        var matchLength = 0;
        var timedOutRuleIds = ImmutableArray.CreateBuilder<long>();

        foreach (var (rule, regex) in _rules)
        {
            Match match;

            try
            {
                match = regex.Match(text);
            }
            catch (RegexMatchTimeoutException)
            {
                timedOutRuleIds.Add(rule.Id);

                continue;
            }

            var precedesSelected = match.Success &&
                                   (match.Index < earliestPosition ||
                                    (match.Index == earliestPosition && (!selected.HasValue || rule.Id < selected.Value.Id)));

            if (precedesSelected)
            {
                selected = rule;
                earliestPosition = match.Index;
                matchLength = match.Length;
            }
        }

        Optional<string> reason = selected.HasValue
            ? FormatReason(text, selected.Value.Id, earliestPosition, matchLength)
            : default;

        return new AutomodMatchResult(selected, timedOutRuleIds.ToImmutable(), reason);
    }

    private static string FormatReason(string text, long ruleId, int position, int length)
    {
        const int contextLimit = 390;
        var context = text;

        if (text.Length > contextLimit)
        {
            const int retainedLength = contextLimit - 6;
            var start = Math.Clamp(position - Math.Max(0, retainedLength - length) / 2, 0, text.Length - retainedLength);
            var end = start + retainedLength;
            context = (start > 0 ? "..." : string.Empty) + text[start..end] + (end < text.Length ? "..." : string.Empty);
        }

        context = string.Join(" ", context.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var delimiter = "`";

        if (context.Contains('`'))
        {
            delimiter = "``";
            context = context.Replace("`", "`\u200D", StringComparison.Ordinal);

            if (context.Length > contextLimit)
                context = context[..(contextLimit - 3)] + "...";

            context = " " + context + " ";
        }

        return $"Automatic action: message matches pattern {ruleId}\n{delimiter}{context}{delimiter}";
    }

    internal static Regex CreateRegex(string pattern, AutomodMatchType matchType)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new FormatException("Automod patterns cannot be empty.");

        var expression = matchType switch
        {
            AutomodMatchType.Word => @"\b" + Regex.Escape(pattern) + @"\b",
            AutomodMatchType.Regex => pattern,
            _ => throw new ArgumentOutOfRangeException(nameof(matchType)),
        };

        var regex = new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        if (regex.IsMatch(string.Empty))
            throw new FormatException("Automod patterns must not match empty text.");

        return regex;
    }
}
