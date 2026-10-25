namespace SylvanBot.Tests.Features.WorkingHours;

using System.Globalization;
using SylvanBot.Application.Features.WorkingHours;
using Shouldly;

[TestFixture]
public sealed class WorkingHoursWindowTests
{
    [TestCase("09:00:00Z", "17:00:00z", null)]
    [TestCase("09:00:00z", "17:00:00+00:00", null)]
    [TestCase("09:00:00+23:59", "17:00:00+23:59", null)]
    [TestCase("09:00:00-23:59", "17:00:00-23:59", null)]
    [TestCase("09:00:00.123456789012345678901234567890Z", "17:00:00Z", null)]
    [TestCase("09:00:00", "17:00:00", "Europe/London")]
    [TestCase("09:00:00", "17:00:00", "GB")]
    public void Parse_CompleteModes_Accepts(string start, string end, string? zone)
    {
        // Act
        ErrorOr<WorkingHoursWindow> parsed = WorkingHoursTimeParser.Parse(start, end, zone);

        // Assert
        parsed.IsError.ShouldBeFalse();
    }

    [TestCase("09:00", "17:00:00Z", null)]
    [TestCase("T09:00:00Z", "17:00:00Z", null)]
    [TestCase("2026-10-25T09:00:00Z", "17:00:00Z", null)]
    [TestCase(" 09:00:00Z", "17:00:00Z", null)]
    [TestCase("09:00:00Z\n", "17:00:00Z", null)]
    [TestCase("０９:00:00Z", "17:00:00Z", null)]
    [TestCase("09:00:00,5Z", "17:00:00Z", null)]
    [TestCase("09:00:00.Z", "17:00:00Z", null)]
    [TestCase("24:00:00Z", "17:00:00Z", null)]
    [TestCase("09:60:00Z", "17:00:00Z", null)]
    [TestCase("09:00:60Z", "17:00:00Z", null)]
    [TestCase("09:00:00+24:00", "17:00:00+24:00", null)]
    [TestCase("09:00:00+01:60", "17:00:00+01:60", null)]
    [TestCase("09:00:00+0100", "17:00:00+0100", null)]
    [TestCase("09:00:00-00:00", "17:00:00Z", null)]
    [TestCase("09:00:00Z", "17:00:00", null)]
    [TestCase("09:00:00", "17:00:00Z", "Europe/London")]
    [TestCase("09:00:00+01:00", "17:00:00Z", null)]
    [TestCase("09:00:00Z", "17:00:00Z", "Europe/London")]
    [TestCase("09:00:00", "17:00:00", null)]
    [TestCase("09:00:00", "17:00:00", "")]
    [TestCase("09:00:00", "17:00:00", "GMT Standard Time")]
    [TestCase("09:00:00", "17:00:00", "Europe/Unknown")]
    public void Parse_InvalidOriginalInput_Rejects(string start, string end, string? zone)
    {
        // Act
        ErrorOr<WorkingHoursWindow> parsed = WorkingHoursTimeParser.Parse(start, end, zone);

        // Assert
        parsed.IsError.ShouldBeTrue();
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

    [TestCase("2026-10-25T08:59:59Z", false)]
    [TestCase("2026-10-25T09:00:00Z", true)]
    [TestCase("2026-10-25T16:59:59Z", true)]
    [TestCase("2026-10-25T17:00:00Z", false)]
    [TestCase("2026-11-03T12:00:00Z", true)]
    public void IsActive_DailyWindow_InclusiveStartExclusiveEnd(string now, bool expected)
    {
        // Arrange
        var window = Window("09:00:00Z", "17:00:00Z");

        // Assert
        window.IsActive(Utc(now)).ShouldBe(expected);
    }

    [TestCase("2026-10-25T22:00:00Z", true)]
    [TestCase("2026-10-26T06:59:59Z", true)]
    [TestCase("2026-10-26T07:00:00Z", false)]
    public void IsActive_OvernightStartup_EvaluatesPresentInterval(string now, bool expected)
    {
        // Arrange
        var window = Window("22:00:00Z", "07:00:00Z");

        // Assert
        window.IsActive(Utc(now)).ShouldBe(expected);
    }

    [TestCase("09:00:00+23:59", "17:00:00+23:59", "2026-10-24T09:01:00Z")]
    [TestCase("09:00:00-23:59", "17:00:00-23:59", "2026-10-26T08:59:00Z")]
    public void IsActive_ExtremeFixedOffsets_UsesDatedArithmetic(string start, string end, string now)
    {
        // Assert
        Window(start, end).IsActive(Utc(now)).ShouldBeTrue();
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
        var window = WorkingHoursTimeParser.Parse(start, end, "Europe/London").Value;

        // Assert
        window.IsActive(Utc(now)).ShouldBe(expected);
    }

    [TestCase("2026-10-25T23:59:59.9999999Z", false)]
    [TestCase("2026-10-26T00:00:00Z", true)]
    [TestCase("2026-10-26T00:30:00Z", false)]
    public void IsActive_FractionCarriesAcrossMidnight_UsesNextDatedTick(string now, bool expected)
    {
        // Arrange
        var window = Window("23:59:59.999999999999Z", "00:30:00Z");

        // Act
        var active = window.IsActive(Utc(now));

        // Assert
        active.ShouldBe(expected);
    }

    public static WorkingHoursWindow Window(string start, string end) => WorkingHoursTimeParser.Parse(start, end, timezone: null).Value;
    private static DateTimeOffset Utc(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}