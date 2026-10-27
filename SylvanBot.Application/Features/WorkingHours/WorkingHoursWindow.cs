using System.Globalization;
using NodaTime;
using NodaTime.TimeZones;

namespace SylvanBot.Application.Features.WorkingHours;

public sealed class WorkingHoursWindow
{
    private static readonly ZoneLocalMappingResolver Resolver = Resolvers.CreateMappingResolver(
        ambiguousTimeResolver: Resolvers.ReturnEarlier,
        skippedTimeResolver: Resolvers.ReturnForwardShifted);

    private readonly DateTimeZone?                     _zone;
    private readonly (int Seconds, long FractionTicks) _start;
    private readonly (int Seconds, long FractionTicks) _end;
    private readonly bool                              _overnight;

    internal WorkingHoursWindow(string start, string end, int? offsetMinutes, string? zoneId)
    {
        if (offsetMinutes.HasValue == zoneId is not null)
            throw new ArgumentException("Exactly one clock definition is required.", nameof(offsetMinutes));

        if (offsetMinutes is < -1439 or > 1439)
            throw new ArgumentOutOfRangeException(nameof(offsetMinutes));

        _zone = zoneId is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId);

        if (zoneId is not null && _zone is null)
            throw new ArgumentException("Unsupported TZDB timezone.", nameof(zoneId));

        StartText     = start;
        EndText       = end;
        OffsetMinutes = offsetMinutes;
        ZoneId        = zoneId;
        _start        = ExecutionTime(start);
        _end          = ExecutionTime(end);
        _overnight    = string.CompareOrdinal(strA: end, strB: start) < 0;
    }

    public string StartText { get; }

    public string EndText { get; }

    public int? OffsetMinutes { get; }

    public string? ZoneId { get; }

    public bool IsActive(DateTimeOffset now)
    {
        if (string.Equals(a: StartText, b: EndText, comparisonType: StringComparison.Ordinal))
            return false;

        var instant = Instant.FromDateTimeOffset(now);

        var date = _zone is null
            ? (instant + Duration.FromMinutes(OffsetMinutes!.Value)).InUtc().Date
            : instant.InZone(_zone).Date;

        for (var day = -1; day <= 0; day++)
        {
            var startDate = date.PlusDays(day);
            var start     = Resolve(date: startDate, time: _start);
            var end       = Resolve(startDate.PlusDays(_overnight ? 1 : 0), time: _end);

            if (end > start && instant >= start && instant < end)
                return true;
        }

        return false;
    }

    private static (int Seconds, long FractionTicks) ExecutionTime(string text)
    {
        var seconds = (int.Parse(text.AsSpan(start: 0, length: 2), provider: CultureInfo.InvariantCulture) * 3600)
            + (int.Parse(text.AsSpan(start: 3, length: 2), provider: CultureInfo.InvariantCulture) * 60)
            + int.Parse(text.AsSpan(start: 6, length: 2), provider: CultureInfo.InvariantCulture);
        var fraction  = text.Length > 8 ? text[9..] : string.Empty;
        var ticksText = fraction.PadRight(totalWidth: 7, paddingChar: '0');
        var ticks     = long.Parse(ticksText.AsSpan(start: 0, length: 7), provider: CultureInfo.InvariantCulture);

        // Canonical fractions have no trailing zero; any remaining digits require rounding up.
        if (fraction.Length > 7)
            ticks++;

        return (seconds, ticks);
    }

    private Instant Resolve(LocalDate date, (int Seconds, long FractionTicks) time)
    {
        var whole = _zone is null
            ? date.AtMidnight().InUtc().ToInstant() + Duration.FromSeconds(time.Seconds - (OffsetMinutes!.Value * 60))
            : _zone.ResolveLocal(date.AtMidnight().PlusSeconds(time.Seconds), resolver: Resolver).ToInstant();

        return whole + Duration.FromTicks(time.FractionTicks);
    }
}