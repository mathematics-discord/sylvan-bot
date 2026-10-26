namespace SylvanBot.Application.Features.WorkingHours;

using System.Globalization;
using NodaTime;
using NodaTime.TimeZones;

public sealed class WorkingHoursWindow
{
    private static readonly ZoneLocalMappingResolver Resolver = Resolvers.CreateMappingResolver(
        Resolvers.ReturnEarlier,
        Resolvers.ReturnForwardShifted);

    private readonly DateTimeZone? _zone;
    private readonly (int Seconds, long FractionTicks) _start;
    private readonly (int Seconds, long FractionTicks) _end;
    private readonly bool _overnight;

    internal WorkingHoursWindow(string start, string end, int? offsetMinutes, string? zoneId)
    {
        if (offsetMinutes.HasValue == (zoneId is not null))
            throw new ArgumentException("Exactly one clock definition is required.", nameof(offsetMinutes));

        if (offsetMinutes is < -1439 or > 1439)
            throw new ArgumentOutOfRangeException(nameof(offsetMinutes));

        _zone = zoneId is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId);

        if (zoneId is not null && _zone is null)
            throw new ArgumentException("Unsupported TZDB timezone.", nameof(zoneId));

        StartText = start;
        EndText = end;
        OffsetMinutes = offsetMinutes;
        ZoneId = zoneId;
        _start = ExecutionTime(start);
        _end = ExecutionTime(end);
        _overnight = string.CompareOrdinal(end, start) < 0;
    }

    public string StartText { get; }

    public string EndText { get; }

    public int? OffsetMinutes { get; }

    public string? ZoneId { get; }

    public bool IsActive(DateTimeOffset now)
    {
        if (string.Equals(StartText, EndText, StringComparison.Ordinal))
            return false;

        var instant = Instant.FromDateTimeOffset(now);
        var date = _zone is null
            ? (instant + Duration.FromMinutes(OffsetMinutes!.Value)).InUtc().Date
            : instant.InZone(_zone).Date;

        for (var day = -1; day <= 0; day++)
        {
            var startDate = date.PlusDays(day);
            var start = Resolve(startDate, _start);
            var end = Resolve(startDate.PlusDays(_overnight ? 1 : 0), _end);

            if (end > start && instant >= start && instant < end)
                return true;
        }

        return false;
    }

    private static (int Seconds, long FractionTicks) ExecutionTime(string text)
    {
        var seconds = (int.Parse(text.AsSpan(0, 2), CultureInfo.InvariantCulture) * 3600)
            + (int.Parse(text.AsSpan(3, 2), CultureInfo.InvariantCulture) * 60)
            + int.Parse(text.AsSpan(6, 2), CultureInfo.InvariantCulture);
        var fraction = text.Length > 8 ? text[9..] : string.Empty;
        var ticksText = fraction.PadRight(7, '0');
        var ticks = long.Parse(ticksText.AsSpan(0, 7), CultureInfo.InvariantCulture);

        // Canonical fractions have no trailing zero; any remaining digits require rounding up.
        if (fraction.Length > 7)
            ticks++;

        return (seconds, ticks);
    }

    private Instant Resolve(LocalDate date, (int Seconds, long FractionTicks) time)
    {
        var whole = _zone is null
            ? date.AtMidnight().InUtc().ToInstant() + Duration.FromSeconds(time.Seconds - (OffsetMinutes!.Value * 60))
            : _zone.ResolveLocal(date.AtMidnight().PlusSeconds(time.Seconds), Resolver).ToInstant();

        return whole + Duration.FromTicks(time.FractionTicks);
    }
}
