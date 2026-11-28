using System.Globalization;
using System.Text.RegularExpressions;

namespace SylvanBot.Application.Features.Automod;

public static partial class AutomodDuration
{
    private const long MaximumMuteSeconds = 28 * 24 * 60 * 60;

    [GeneratedRegex(
        @"\A\s*(?<amount>[0-9]+)\s*(?<unit>s|seconds?|m|minutes?|h|hours?|d|days?|w|weeks?)\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex DurationPattern { get; }

    public static Optional<DateTimeOffset> BanExpiry(Optional<long> seconds, DateTimeOffset now)
    {
        if (!seconds.HasValue)
            return default;

        return seconds.Value <= 0
            ? throw new ArgumentOutOfRangeException(nameof(seconds))
            : new Optional<DateTimeOffset>(now.AddSeconds(seconds.Value));
    }

    public static DateTimeOffset MuteExpiry(Optional<long> seconds, DateTimeOffset now)
    {
        if (!seconds.HasValue || seconds.Value <= 0 || seconds.Value > MaximumMuteSeconds)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Stored mute duration must be positive and at most 28 days.");

        return now.AddSeconds(seconds.Value);
    }

    public static bool CoversExpiry(Optional<DateTimeOffset> existingExpiry, DateTimeOffset requestedExpiry) =>
        existingExpiry.HasValue && existingExpiry.Value >= requestedExpiry;

    public static Optional<long> ResolveSeconds(AutomodAction action, Optional<string> duration, DateTimeOffset now)
    {
        if (action is AutomodAction.Delete or AutomodAction.Note)
            return default;

        if (action is not (AutomodAction.Mute or AutomodAction.Ban))
            throw new ArgumentOutOfRangeException(nameof(action));

        var invalidDuration = action == AutomodAction.Ban
            ? "Enter permanent or a positive whole-number duration such as 30m or 2 days (s, m, h, d or w)."
            : "Enter a positive whole-number duration such as 30m or 2 days (s, m, h, d or w).";

        if (!duration.HasValue || string.IsNullOrWhiteSpace(duration.Value))
            throw new FormatException(invalidDuration);

        if (action == AutomodAction.Ban && string.Equals(duration.Value.Trim(), "permanent", StringComparison.OrdinalIgnoreCase))
            return default;

        Match match = DurationPattern.Match(duration.Value);

        if (!match.Success)
            throw new FormatException(invalidDuration);

        var amount = long.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value.ToLowerInvariant();

        var multiplier = unit[0] switch
        {
            's' => 1L,
            'm' => 60L,
            'h' => 3600L,
            'd' => 86400L,
            'w' => 604800L,
            _ => throw new FormatException("Choose seconds, minutes, hours, days or weeks."),
        };

        var seconds = checked(amount * multiplier);

        if (seconds <= 0)
            throw new FormatException("Mute and Ban durations must be positive.");

        if (action == AutomodAction.Mute && seconds > MaximumMuteSeconds)
            throw new FormatException("Mute duration cannot exceed 28 days.");

        var maximumBanSeconds = (DateTimeOffset.MaxValue - now).Ticks / TimeSpan.TicksPerSecond;

        if (action == AutomodAction.Ban && seconds > maximumBanSeconds)
            throw new FormatException("The ban duration is too large; choose a shorter duration or enter permanent.");

        return new Optional<long>(seconds);
    }
}
