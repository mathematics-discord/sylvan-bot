using System.Globalization;
using System.Text.RegularExpressions;
using NodaTime;

namespace SylvanBot.Application.Features.WorkingHours;

public static partial class WorkingHoursTimeParser
{
    public static Result<WorkingHoursWindow> Parse(string start, string end, string? timezone)
    {
        (string Time, int? OffsetMinutes)? first = ParseEndpoint(start);
        (string Time, int? OffsetMinutes)? last = ParseEndpoint(end);

        if (first is null || last is null)
        {
            return new Result<WorkingHoursWindow>(
                OperationFailureException.Validation(
                    description: "Use HH:MM:SS[.fraction], with Z or ±HH:MM for fixed offsets. No dates, whitespace, leap seconds or -00:00."
                ));
        }

        if (first.Value.OffsetMinutes != last.Value.OffsetMinutes)
        {
            return new Result<WorkingHoursWindow>(
                OperationFailureException.Validation(description: "Both endpoints must have numerically equal offsets, or both must omit offsets."));
        }

        if (first.Value.OffsetMinutes.HasValue)
        {
            if (timezone is not null)
            {
                return new Result<WorkingHoursWindow>(
                    OperationFailureException.Validation(description: "Do not combine fixed offsets with a named timezone."));
            }
        }
        else if (timezone is null || DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone) is null)
        {
            return new Result<WorkingHoursWindow>(
                OperationFailureException.Validation(
                    description: "Offset-free times require a supported IANA timezone ID (for example Europe/London)."));
        }

        return new WorkingHoursWindow(start: first.Value.Time, end: last.Value.Time, offsetMinutes: first.Value.OffsetMinutes, zoneId: timezone);
    }

    internal static (string Time, int? OffsetMinutes)? ParseEndpoint(string input)
    {
        var match = ClockPattern().Match(input);

        if (!match.Success)
            return null;

        var hour = int.Parse(s: match.Groups["hour"].Value, provider: CultureInfo.InvariantCulture);
        var minute = int.Parse(s: match.Groups["minute"].Value, provider: CultureInfo.InvariantCulture);
        var second = int.Parse(s: match.Groups["second"].Value, provider: CultureInfo.InvariantCulture);

        if (hour > 23 || minute > 59 || second > 59)
            return null;

        int? offsetMinutes = null;
        var offset = match.Groups["offset"].Value;

        if (offset.Length > 1)
        {
            var offsetHour = int.Parse(offset.AsSpan(start: 1, length: 2), provider: CultureInfo.InvariantCulture);
            var offsetMinute = int.Parse(offset.AsSpan(start: 4, length: 2), provider: CultureInfo.InvariantCulture);

            if (offsetHour > 23 || offsetMinute > 59 || string.Equals(a: offset, "-00:00", comparisonType: StringComparison.Ordinal))
                return null;

            offsetMinutes = ((offsetHour * 60) + offsetMinute) * (offset[0] == '-' ? -1 : 1);
        }
        else if (offset.Length == 1)
            offsetMinutes = 0;

        var fraction = match.Groups["fraction"].Value.TrimEnd('0');
        var clock = input[..8];

        return (fraction.Length == 0 ? clock : clock + "." + fraction, offsetMinutes);
    }

    [GeneratedRegex(
        @"\A(?<hour>[0-9]{2}):(?<minute>[0-9]{2}):(?<second>[0-9]{2})(?:\.(?<fraction>[0-9]+))?(?<offset>Z|z|[+-][0-9]{2}:[0-9]{2})?\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ClockPattern();
}