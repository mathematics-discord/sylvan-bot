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
            }
        }

        return new AutomodMatchResult(selected, timedOutRuleIds.ToImmutable());
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