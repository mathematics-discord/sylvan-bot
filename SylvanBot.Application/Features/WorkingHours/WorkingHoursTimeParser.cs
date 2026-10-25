namespace SylvanBot.Application.Features.WorkingHours;

using System.Globalization;
using System.Text.RegularExpressions;
using ErrorOr;
using NodaTime;

public static partial class WorkingHoursTimeParser
{
    public static ErrorOr<WorkingHoursWindow> Parse(string start, string end, string? timezone)
    {
        (string Time, int? OffsetMinutes)? first = ParseEndpoint(start);
        (string Time, int? OffsetMinutes)? last = ParseEndpoint(end);

        if (first is null || last is null)
            return Error.Validation(description: "Use HH:MM:SS[.fraction], with Z or ±HH:MM for fixed offsets. No dates, whitespace, leap seconds or -00:00.");

        if (first.Value.OffsetMinutes != last.Value.OffsetMinutes)
            return Error.Validation(description: "Both endpoints must have numerically equal offsets, or both must omit offsets.");

        if (first.Value.OffsetMinutes.HasValue)
        {
            if (timezone is not null)
                return Error.Validation(description: "Do not combine fixed offsets with a named timezone.");
        }
        else if (timezone is null || DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone) is null)
        {
            return Error.Validation(description: "Offset-free times require a supported IANA timezone ID (for example Europe/London).");
        }

        return new WorkingHoursWindow(first.Value.Time, last.Value.Time, first.Value.OffsetMinutes, timezone);
    }

    internal static (string Time, int? OffsetMinutes)? ParseEndpoint(string input)
    {
        var match = ClockPattern().Match(input);
        if (!match.Success)
            return null;

        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59 || second > 59)
            return null;

        int? offsetMinutes = null;
        var offset = match.Groups["offset"].Value;
        if (offset.Length > 1)
        {
            var offsetHour = int.Parse(offset.AsSpan(1, 2), CultureInfo.InvariantCulture);
            var offsetMinute = int.Parse(offset.AsSpan(4, 2), CultureInfo.InvariantCulture);
            if (offsetHour > 23 || offsetMinute > 59 || string.Equals(offset, "-00:00", StringComparison.Ordinal))
                return null;

            offsetMinutes = ((offsetHour * 60) + offsetMinute) * (offset[0] == '-' ? -1 : 1);
        }
        else if (offset.Length == 1)
        {
            offsetMinutes = 0;
        }

        var fraction = match.Groups["fraction"].Value.TrimEnd('0');
        var clock = input[..8];
        return (fraction.Length == 0 ? clock : clock + "." + fraction, offsetMinutes);
    }

    [GeneratedRegex(@"\A(?<hour>[0-9]{2}):(?<minute>[0-9]{2}):(?<second>[0-9]{2})(?:\.(?<fraction>[0-9]+))?(?<offset>Z|z|[+-][0-9]{2}:[0-9]{2})?\z", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex ClockPattern();
}
