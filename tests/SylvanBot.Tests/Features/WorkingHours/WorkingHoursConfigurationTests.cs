using SylvanBot.Application.Features.WorkingHours;

namespace SylvanBot.Tests.Features.WorkingHours;

[TestFixture]
public sealed class WorkingHoursConfigurationTests
{
    [Test]
    public void ChangeRoles_InvalidRelationships_PreservesSettings()
    {
        // Arrange
        var settings = new WorkingHoursSettings { GuildId = 123 };
        settings.SetPingRole(10).IsError.ShouldBeFalse();
        settings.ChangeEligibleRole(roleId: 20, add: true).IsError.ShouldBeFalse();
        settings.ChangeExemptRole(roleId: 20, add: true).IsError.ShouldBeFalse();

        // Act
        settings.SetPingRole(20).IsError.ShouldBeTrue();
        settings.SetPingRole(123).IsError.ShouldBeTrue();
        settings.ChangeEligibleRole(roleId: 10, add: true).IsError.ShouldBeTrue();
        settings.ChangeEligibleRole(roleId: 123, add: true).IsError.ShouldBeTrue();
        settings.ChangeEligibleRole(roleId: 20, add: false).IsError.ShouldBeTrue();
        settings.ChangeExemptRole(roleId: 30, add: true).IsError.ShouldBeTrue();

        // Assert
        settings.PingRoleId.ShouldBe(10UL);
        settings.EligibleRoles.Count.ShouldBe(1);

        var role = settings.EligibleRoles
           .Single();
        role.RoleId.ShouldBe(20UL);
        role.IsExempt.ShouldBeTrue();
        settings.IsComplete.ShouldBeTrue();
    }

    [Test]
    public void ChangeRoles_EnabledGuild_RequiresDisablingBeforeTargetOrLastEligibilityRemoval()
    {
        // Arrange
        var settings = new WorkingHoursSettings { GuildId = 123 };
        settings.SetPingRole(10);
        settings.ChangeEligibleRole(roleId: 20, add: true);
        settings.Enabled = true;

        // Act / Assert
        settings.SetPingRole(11).IsError.ShouldBeTrue();
        settings.ChangeEligibleRole(roleId: 20, add: false).IsError.ShouldBeTrue();
        settings.PingRoleId.ShouldBe(10UL);
        settings.EligibleRoles.Count.ShouldBe(1);

        settings.Enabled = false;
        settings.SetPingRole(11).IsError.ShouldBeFalse();
        settings.ChangeEligibleRole(roleId: 20, add: false).IsError.ShouldBeFalse();
        settings.IsComplete.ShouldBeFalse();
    }

    [TestCase(false, true, false, true)]
    [TestCase(false, true, true, true)]
    [TestCase(true, true, false, null)]
    [TestCase(true, true, true, null)]
    [TestCase(false, false, false, true)]
    [TestCase(true, false, false, null)]
    public void DesiredPingRole_GlobalDisable_IgnoresSavedSchedule(bool exempt, bool scheduled, bool active, bool? expected)
    {
        // Arrange
        var settings = new WorkingHoursSettings { Enabled = false };

        // Act
        var desired = settings.DesiredPingRole(eligible: true, exempt: exempt, scheduleEnabled: scheduled, active: active);

        // Assert
        desired.ShouldBe(expected);
    }

    [Test]
    public void DesiredPingRole_ReenabledGuild_ResumesSavedScheduleAtCurrentTime()
    {
        // Arrange
        var settings   = new WorkingHoursSettings { Enabled = false };
        var preference = new WorkingHoursPreference(guildId: 123, memberId: 456);
        preference.EnableSchedule(WorkingHoursTimeParser.Parse("09:00:00Z", "17:00:00Z", timezone: null).Value);
        var now    = new DateTimeOffset(year: 2026, month: 10, day: 28, hour: 18, minute: 0, second: 0, offset: TimeSpan.Zero);
        var active = preference.Window!.IsActive(now);

        // Act / Assert
        settings.DesiredPingRole(eligible: true, exempt: false, scheduleEnabled: preference.Enabled, active: active).ShouldBe(true);
        preference.Enabled.ShouldBeTrue();
        settings.Enabled = true;
        settings.DesiredPingRole(eligible: true, exempt: false, scheduleEnabled: preference.Enabled, active: active).ShouldBe(false);
    }

    [Test]
    public void DisableSchedule_ConfirmedLoss_RetainsWindowWithoutReactivatingOnReappointment()
    {
        // Arrange
        var preference = new WorkingHoursPreference(guildId: 123, memberId: 456);
        preference.EnableSchedule(WorkingHoursTimeParser.Parse("22:00:00.123456789Z", "07:00:00Z", timezone: null).Value);

        // Act
        preference.DisableSchedule();

        // Assert
        preference.Enabled.ShouldBeFalse();
        preference.Window!.StartText.ShouldBe("22:00:00.123456789");
        var settings = new WorkingHoursSettings { Enabled = true };
        settings.DesiredPingRole(eligible: true, exempt: false, scheduleEnabled: preference.Enabled, active: false).ShouldBe(true);
        settings.DesiredPingRole(eligible: true, exempt: true, scheduleEnabled: preference.Enabled, active: false).ShouldBeNull();
    }
}