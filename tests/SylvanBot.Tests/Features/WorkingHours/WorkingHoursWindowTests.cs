using System.Globalization;
using CsCheck;
using SylvanBot.Application.Features.WorkingHours;

namespace SylvanBot.Tests.Features.WorkingHours;

[TestFixture]
public sealed class WorkingHoursWindowTests
{
    [Test]
    public void IsActive_FixedOffsets_MatchesCircularInterval()
    {
        // Arrange
        Gen<int> seconds = Gen.Int[0, 86399];
        Gen<(int, int, int, int)> inputs = seconds.Select(seconds, seconds, Gen.Int[-1439, 1439]);

        // Act / Assert
        inputs.Sample(
            (start, end, observation, offset) =>
            {
                var magnitude = Math.Abs(offset);
                var sign = offset < 0 ? "-" : "+";
                var suffix = string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude / 60:00}:{magnitude % 60:00}");
                var window = Window(Clock(start) + suffix, Clock(end) + suffix);
                var midnight = Utc("2026-10-25T00:00:00Z").AddMinutes(-offset);
                var duration = (end - start + 86400) % 86400;

                foreach (var second in new[] { start, end, observation })
                foreach (var tick in new[] { -1, 0, 1 })
                {
                    var localTicks = (second * TimeSpan.TicksPerSecond) + tick;
                    var distance = (localTicks - (start * TimeSpan.TicksPerSecond) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
                    var expected = distance < duration * TimeSpan.TicksPerSecond;
                    window.IsActive(midnight.AddTicks(localTicks)).ShouldBe(expected);
                }
            },
            iter: 1000,
            threads: 1);
    }

    [Test]
    public void Parse_TrailingFractionZeros_PreserveScheduleAndResponses()
    {
        // Arrange
        Gen<string> fraction = Gen.Int[0, 9]
            .Array[1, 24]
            .Select(string.Concat);

        Gen<(int, int, string, string, int)> inputs = Gen.Int[0, 86399].Select(Gen.Int[0, 86399], fraction, fraction, Gen.Int[1, 8]);

        // Act / Assert
        inputs.Sample(
            (start, end, startFraction, endFraction, zeros) =>
            {
                var first = $"{Clock(start)}.{startFraction}";
                var last = $"{Clock(end)}.{endFraction}";
                var padding = new string('0', zeros);
                var original = Window(first + "Z", last + "Z");
                var equivalent = Window(first + padding + "Z", last + padding + "Z");
                equivalent.StartText.ShouldBe(original.StartText);
                equivalent.EndText.ShouldBe(original.EndText);
                var now = Utc("2026-10-25T00:00:00Z").AddSeconds(start);
                equivalent.IsActive(now.AddTicks(1)).ShouldBe(original.IsActive(now.AddTicks(1)));
                WorkingHoursResponses.Rejection(equivalent, "Rat").ShouldBe(WorkingHoursResponses.Rejection(original, "Rat"));
                WorkingHoursResponses.Accepted(equivalent).ShouldBe(WorkingHoursResponses.Accepted(original));
            },
            iter: 1000,
            threads: 1);
    }

    private static string Clock(int seconds) => TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(seconds))
        .ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    [TestCase("09:00:00Z", "17:00:00z", arg3: null)]
    [TestCase("09:00:00z", "17:00:00+00:00", arg3: null)]
    [TestCase("09:00:00+23:59", "17:00:00+23:59", arg3: null)]
    [TestCase("09:00:00-23:59", "17:00:00-23:59", arg3: null)]
    [TestCase("09:00:00.123456789012345678901234567890Z", "17:00:00Z", arg3: null)]
    [TestCase("09:00:00", "17:00:00", "Europe/London")]
    [TestCase("09:00:00", "17:00:00", "GB")]
    public void Parse_CompleteModes_Accepts(string start, string end, string? zone)
    {
        // Act
        Result<WorkingHoursWindow> parsed = WorkingHoursTimeParser.Parse(start: start, end: end, timezone: zone);

        // Assert
        parsed.IsSuccessful.ShouldBeTrue();
    }

    [TestCase("09:00", "17:00:00Z", arg3: null)]
    [TestCase("T09:00:00Z", "17:00:00Z", arg3: null)]
    [TestCase("2026-10-25T09:00:00Z", "17:00:00Z", arg3: null)]
    [TestCase(" 09:00:00Z", "17:00:00Z", arg3: null)]
    [TestCase("09:00:00Z\n", "17:00:00Z", arg3: null)]
    [TestCase("０９:00:00Z", "17:00:00Z", arg3: null)]
    [TestCase("09:00:00,5Z", "17:00:00Z", arg3: null)]
    [TestCase("09:00:00.Z", "17:00:00Z", arg3: null)]
    [TestCase("24:00:00Z", "17:00:00Z", arg3: null)]
    [TestCase("09:60:00Z", "17:00:00Z", arg3: null)]
    [TestCase("09:00:60Z", "17:00:00Z", arg3: null)]
    [TestCase("09:00:00+24:00", "17:00:00+24:00", arg3: null)]
    [TestCase("09:00:00+01:60", "17:00:00+01:60", arg3: null)]
    [TestCase("09:00:00+0100", "17:00:00+0100", arg3: null)]
    [TestCase("09:00:00-00:00", "17:00:00Z", arg3: null)]
    [TestCase("09:00:00Z", "17:00:00", arg3: null)]
    [TestCase("09:00:00", "17:00:00Z", "Europe/London")]
    [TestCase("09:00:00+01:00", "17:00:00Z", arg3: null)]
    [TestCase("09:00:00Z", "17:00:00Z", "Europe/London")]
    [TestCase("09:00:00", "17:00:00", arg3: null)]
    [TestCase("09:00:00", "17:00:00", "")]
    [TestCase("09:00:00", "17:00:00", "GMT Standard Time")]
    [TestCase("09:00:00", "17:00:00", "Europe/Unknown")]
    public void Parse_InvalidOriginalInput_Rejects(string start, string end, string? zone)
    {
        // Act
        Result<WorkingHoursWindow> parsed = WorkingHoursTimeParser.Parse(start: start, end: end, timezone: zone);

        // Assert
        parsed.IsSuccessful.ShouldBeFalse();
    }

    [Test]
    public void Parse_EquivalentFractions_HaveExactEqualityAndZeroDuration()
    {
        // Act
        var window = Window("09:00:00.1Z", "09:00:00.100Z");

        // Assert
        window.StartText.ShouldBe(window.EndText);
        window.IsActive(Utc("2026-10-25T09:00:00Z")).ShouldBeFalse();
    }

    [Test]
    public void IsActive_SubTickFraction_CannotTransitionEarly()
    {
        // Arrange
        var window = Window("09:00:00.0000000000001Z", "10:00:00.9999999999999Z");

        // Assert
        window.IsActive(Utc("2026-10-25T09:00:00Z")).ShouldBeFalse();
        window.IsActive(Utc("2026-10-25T09:00:00.0000001Z")).ShouldBeTrue();
        window.IsActive(Utc("2026-10-25T10:00:00.9999999Z")).ShouldBeTrue();
        window.IsActive(Utc("2026-10-25T10:00:01Z")).ShouldBeFalse();
    }

    [TestCase("01:30:00", "02:15:00", "2026-03-29T01:40:00Z", false)]
    [TestCase("01:30:00", "03:00:00", "2026-03-29T01:29:59Z", false)]
    [TestCase("01:30:00", "03:00:00", "2026-03-29T01:30:00Z", true)]
    [TestCase("01:15:00", "01:45:00", "2026-10-25T00:15:00Z", true)]
    [TestCase("01:15:00", "01:45:00", "2026-10-25T00:45:00Z", false)]
    [TestCase("01:15:00", "01:45:00", "2026-10-25T01:30:00Z", false)]
    [TestCase("09:00:00", "17:00:00", "2026-07-01T08:00:00Z", true)]
    [TestCase("09:00:00", "17:00:00", "2026-12-01T08:00:00Z", false)]
    public void IsActive_LondonTransitions_ResolvesPairedOccurrences(string start, string end, string now, bool expected)
    {
        // Arrange
        var window = WorkingHoursTimeParser.Parse(start: start, end: end, "Europe/London").Value;

        // Assert
        window.IsActive(Utc(now)).ShouldBe(expected);
    }

    [TestCase("2026-10-25T23:59:59.9999999Z", arg2: false)]
    [TestCase("2026-10-26T00:00:00Z", arg2: true)]
    [TestCase("2026-10-26T00:30:00Z", arg2: false)]
    public void IsActive_FractionCarriesAcrossMidnight_UsesNextDatedTick(string now, bool expected)
    {
        // Arrange
        var window = Window("23:59:59.999999999999Z", "00:30:00Z");

        // Act
        var active = window.IsActive(Utc(now));

        // Assert
        active.ShouldBe(expected);
    }

    public static WorkingHoursWindow Window(string start, string end) => WorkingHoursTimeParser.Parse(start: start, end: end, timezone: null).Value;

    private static DateTimeOffset Utc(string text) => DateTimeOffset.Parse(input: text, formatProvider: CultureInfo.InvariantCulture);
}