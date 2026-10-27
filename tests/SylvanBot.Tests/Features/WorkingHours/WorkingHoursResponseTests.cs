using SylvanBot.Application.Features.WorkingHours;

namespace SylvanBot.Tests.Features.WorkingHours;

using static WorkingHoursWindowTests;

[TestFixture]
public sealed class WorkingHoursResponseTests
{
    [TestCase("00:00:00Z", "```text\n@Rat is not in the time file.\nThis incident will be reported.\n```")]
    [TestCase("00:00:59.999999999999999999Z", "```text\n@Rat is not in the time file.\nThis incident will be reported.\n```")]
    [TestCase("00:01:00Z", "VERY FUNNY. AREN'T YOU FUNNY. HEHEHEHE.")]
    [TestCase("00:09:59.999999999999999999Z", "VERY FUNNY. AREN'T YOU FUNNY. HEHEHEHE.")]
    [TestCase("00:10:00Z", "Okay nah that's too low vro")]
    [TestCase("00:19:59.999999999999999999Z", "Okay nah that's too low vro")]
    [TestCase("00:20:00Z", arg2: null)]
    [TestCase("00:59:59.999999999999999999Z", arg2: null)]
    [TestCase("01:00:00Z", arg2: null)]
    public void Rejection_ExactDurationBands_ReturnsOnlyApplicableRejection(string end, string? expected)
    {
        // Assert
        WorkingHoursResponses.Rejection(Window("00:00:00Z", end: end), "Rat").ShouldBe(expected);
    }

    [TestCase("00:20:00Z", arg2: true)]
    [TestCase("00:59:59.999999999999Z", arg2: true)]
    [TestCase("01:00:00Z", arg2: false)]
    public void Accepted_SubHourOnly_ContainsReflection(string end, bool expected)
    {
        // Act
        var response = string.Join(separator: '\n', WorkingHoursResponses.Accepted(Window("00:00:00Z", end: end)));

        // Assert
        response.Contains("revolutionary potential", comparisonType: StringComparison.Ordinal).ShouldBe(expected);
    }

    [TestCase("22:00:00Z", "07:00:00Z", true, false)]
    [TestCase("23:00:00Z", "00:00:00Z", true, false)]
    [TestCase("06:00:00Z", "07:00:00Z", true, false)]
    [TestCase("18:00:00Z", "02:00:00Z", false, true)]
    [TestCase("18:00:00Z", "00:00:00Z", false, true)]
    [TestCase("18:00:00Z", "22:00:00Z", false, false)]
    [TestCase("18:00:00Z", "22:00:00.00000000001Z", false, true)]
    [TestCase("21:59:59Z", "07:00:00Z", false, false)]
    [TestCase("07:00:00Z", "06:59:59Z", false, true)]
    public void Accepted_NightAndBedtime_UsesCircularPrecedence(string start, string end, bool night, bool bedtime)
    {
        // Act
        var response = string.Join(separator: '\n', WorkingHoursResponses.Accepted(Window(start: start, end: end)));

        // Assert
        response.Contains("night shift", comparisonType: StringComparison.Ordinal).ShouldBe(night);
        response.Contains("Go to fucking bed", comparisonType: StringComparison.Ordinal).ShouldBe(bedtime);
    }

    [TestCase("00:00:00Z", "06:00:00Z", "/regulation/12", false)]
    [TestCase("00:00:00Z", "06:00:00.00000000001Z", "/regulation/12", true)]
    [TestCase("00:00:00Z", "06:51:25.714285714285714285Z", "/regulation/4", false)]
    [TestCase("00:00:00Z", "06:51:25.714285714285714286Z", "/regulation/4", true)]
    [TestCase("00:00:00Z", "13:00:00Z", "/regulation/10", false)]
    [TestCase("00:00:00Z", "13:00:00.00000000001Z", "/regulation/10", true)]
    [TestCase("22:00:00Z", "06:00:00Z", "/regulation/6", false)]
    [TestCase("22:00:00Z", "06:00:00.00000000001Z", "/regulation/6", true)]
    [TestCase("18:00:00Z", "02:00:00Z", "/regulation/6", false)]
    [TestCase("18:00:00Z", "02:00:00.00000000001Z", "/regulation/6", true)]
    [TestCase("22:00:00Z", "00:59:59.99999999999Z", "/regulation/7", false)]
    [TestCase("22:00:00Z", "01:00:00Z", "/regulation/7", true)]
    [TestCase("10:00:00Z", "10:20:00Z", "/regulation/11", true)]
    public void Accepted_ExactLegalThresholds_AccumulatesApplicableNotices(string start, string end, string link, bool expected)
    {
        // Act
        var response = string.Join(separator: '\n', WorkingHoursResponses.Accepted(Window(start: start, end: end)));

        // Assert
        response.Contains(value: link, comparisonType: StringComparison.Ordinal).ShouldBe(expected);
    }

    [Test]
    public void Accepted_LongNightDuty_ContainsAllSixCategoriesAndOneConclusion()
    {
        // Act
        IReadOnlyList<string> messages = WorkingHoursResponses.Accepted(Window("16:00:00Z", "07:00:00Z"));
        var                   response = string.Join(separator: '\n', values: messages);

        // Assert
        foreach (var regulation in new[] { 4, 6, 7, 10, 11, 12 })
            response.ShouldContain($"/regulation/{regulation})");

        messages.Count(message => message.StartsWith("Your availability has nevertheless", comparisonType: StringComparison.Ordinal))
           .ShouldBe(1);

        messages.All(message => message.Length < 2000).ShouldBeTrue();

        response.ShouldContain("11 consecutive hours of rest in each 24-hour period");
        response.ShouldContain("17-week");
    }

    [TestCase(false, false, false, false, false)]
    [TestCase(false, true, true, true, false)]
    [TestCase(true, false, true, true, true)]
    [TestCase(true, false, true, false, false)]
    [TestCase(true, true, true, true, true)]
    [TestCase(true, true, true, false, false)]
    [TestCase(true, false, false, false, true)]
    [TestCase(true, true, false, false, null)]
    public void DesiredPingRole_FullRoleTable_ReturnsPolicy(bool eligible, bool exempt, bool enabled, bool active, bool? expected)
    {
        // Arrange
        var settings = new WorkingHoursSettings { Enabled = true };

        // Act / Assert
        settings.DesiredPingRole(eligible: eligible, exempt: exempt, scheduleEnabled: enabled, active: active).ShouldBe(expected);
    }
}