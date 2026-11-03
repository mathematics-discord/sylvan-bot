using System.Globalization;
using System.Text;
using SylvanBot.Application.Features.Automod;

namespace SylvanBot.Features.Automod;

internal static class AutomodRuleList
{
    public const string Heading = "### Automod rules\n";
    private const int BodyLimit = 3900;
    private const int EntriesPerPage = 20;

    public static (string Text, int Index, int Count) CreatePage(IEnumerable<AutomodRule> rules, int requestedPage)
    {
        List<string> pages = [];
        var body = new StringBuilder();
        var entryCount = 0;
        IEnumerable<AutomodRule> orderedRules = rules
            .OrderBy(rule => rule.Id);

        foreach (var rule in orderedRules)
        {
            foreach (var entry in FormatEntries(rule))
            {
                var pageFull = entryCount == EntriesPerPage || body.Length + entry.Length + 1 > BodyLimit;

                if (pageFull)
                {
                    pages.Add(body.ToString());
                    body.Clear();
                    entryCount = 0;
                }

                if (entryCount > 0)
                    body.Append('\n');

                body.Append(entry);
                entryCount++;
            }
        }

        if (body.Length > 0)
            pages.Add(body.ToString());

        if (pages.Count == 0)
            pages.Add("No automod rules in this guild.");

        var index = Math.Clamp(requestedPage, 0, pages.Count - 1);
        var text = string.Create(CultureInfo.InvariantCulture, $"{Heading}Page {index + 1} of {pages.Count}\n\n{pages[index]}");
        return (text, index, pages.Count);
    }

    private static IEnumerable<string> FormatEntries(AutomodRule rule)
    {
        var action = DescribeAction(rule);
        var label = string.Create(CultureInfo.InvariantCulture, $"- #{rule.Id} ? {rule.MatchType} ? {action}");
        const string continuation = " (continued)";
        const string opening = " ? ||`";
        const string closing = "`||";
        var patternLimit = BodyLimit - label.Length - continuation.Length - opening.Length - closing.Length - 1;
        var pattern = new StringBuilder();
        var continued = false;

        foreach (var rune in rule.Pattern.EnumerateRunes())
        {
            // Display dangerous delimiters and control characters literally without changing the stored rule.
            var escaped = rune.Value switch
            {
                '`' => "\\u0060",
                '|' => "\\u007C",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ when Rune.IsControl(rune) => "\\u" + rune.Value.ToString("X4", CultureInfo.InvariantCulture),
                _ => rune.ToString(),
            };

            if (pattern.Length + escaped.Length > patternLimit)
            {
                yield return label + (continued ? continuation : string.Empty) + opening + pattern + closing;
                pattern.Clear();
                continued = true;
            }

            pattern.Append(escaped);
        }

        yield return label + (continued ? continuation : string.Empty) + opening + pattern + closing;
    }

    private static string DescribeAction(AutomodRule rule)
    {
        if (rule.Action is AutomodAction.Delete or AutomodAction.Note)
            return rule.Action.ToString();

        DotNext.Optional<long> duration = rule.DurationSeconds is { } seconds ? new DotNext.Optional<long>(seconds) : default;

        if (!duration.HasValue)
            return rule.Action == AutomodAction.Ban ? "Ban, permanent" : "Mute, unspecified duration";

        var value = duration.Value;
        var unit = "second";

        if (value > 0 && value % 86400 == 0)
        {
            value /= 86400;
            unit = "day";
        }
        else if (value > 0 && value % 3600 == 0)
        {
            value /= 3600;
            unit = "hour";
        }
        else if (value > 0 && value % 60 == 0)
        {
            value /= 60;
            unit = "minute";
        }

        var plural = value == 1 ? string.Empty : "s";
        return string.Create(CultureInfo.InvariantCulture, $"{rule.Action}, {value} {unit}{plural}");
    }
}
