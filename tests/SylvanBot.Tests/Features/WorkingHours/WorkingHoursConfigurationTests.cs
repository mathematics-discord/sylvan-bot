using SylvanBot.Application.Features.WorkingHours;

namespace SylvanBot.Tests.Features.WorkingHours;

[TestFixture]
public sealed class WorkingHoursConfigurationTests
{
    [TestCase(false, true, false, true)]
    [TestCase(false, true, true, true)]
    [TestCase(true, true, false, null)]
    [TestCase(true, true, true, null)]
    [TestCase(false, false, false, true)]
    [TestCase(true, false, false, null)]
    public void DesiredPingRole_GlobalDisable_IgnoresSavedSchedule(bool exempt, bool scheduled, bool active, bool? expected)
    {
        // Arrange
        WorkingHoursSettings settings = new WorkingHoursSettings { Enabled = false };

        // Act
        Optional<bool> desired = settings.DesiredPingRole(eligible: true, exempt: exempt, scheduleEnabled: scheduled, active: active);

        // Assert
        desired.ShouldBe(expected.HasValue ? new Optional<bool>(expected.Value) : default);
    }

    [Test]
    public void ChangeRoles_InvalidRelationships_PreservesSettings()
    {
        // Arrange
        WorkingHoursSettings settings = new WorkingHoursSettings { GuildId = 123 };
        settings.SetPingRole(10);
        settings.ChangeEligibleRole(roleId: 20, add: true);
        settings.ChangeExemptRole(roleId: 20, add: true);

        // Act
        Should.Throw<BotCommandException>(() => settings.SetPingRole(20));
        Should.Throw<BotCommandException>(() => settings.SetPingRole(123));
        Should.Throw<BotCommandException>(() => settings.ChangeEligibleRole(roleId: 10, add: true));
        Should.Throw<BotCommandException>(() => settings.ChangeEligibleRole(roleId: 123, add: true));
        Should.Throw<BotCommandException>(() => settings.ChangeEligibleRole(roleId: 20, add: false));
        Should.Throw<BotCommandException>(() => settings.ChangeExemptRole(roleId: 30, add: true));

        // Assert
        settings.PingRoleId.ShouldBe(10UL);
        settings.EligibleRoles.Count.ShouldBe(1);

        WorkingHoursEligibleRole role = settings.EligibleRoles.Single();
        role.RoleId.ShouldBe(20UL);
        role.IsExempt.ShouldBeTrue();
        settings.IsComplete.ShouldBeTrue();
    }

    [Test]
    public void ChangeRoles_EnabledGuild_RequiresDisablingBeforeTargetOrLastEligibilityRemoval()
    {
        // Arrange
        WorkingHoursSettings settings = new WorkingHoursSettings { GuildId = 123 };
        settings.SetPingRole(10);
        settings.ChangeEligibleRole(roleId: 20, add: true);
        settings.Enabled = true;

        // Act / Assert
        Should.Throw<BotCommandException>(() => settings.SetPingRole(11));
        Should.Throw<BotCommandException>(() => settings.ChangeEligibleRole(roleId: 20, add: false));
        settings.PingRoleId.ShouldBe(10UL);
        settings.EligibleRoles.Count.ShouldBe(1);

        settings.Enabled = false;
        settings.SetPingRole(11);
        settings.ChangeEligibleRole(roleId: 20, add: false);
        settings.IsComplete.ShouldBeFalse();
    }

    [Test]
    public void DesiredPingRole_ReenabledGuild_ResumesSavedScheduleAtCurrentTime()
    {
        // Arrange
        WorkingHoursSettings settings = new WorkingHoursSettings { Enabled = false };
        WorkingHoursPreference preference = new WorkingHoursPreference(guildId: 123, memberId: 456);
        preference.EnableSchedule(WorkingHoursTimeParser.Parse("09:00:00Z", "17:00:00Z", timezone: null));
        DateTimeOffset now = new DateTimeOffset(year: 2026, month: 10, day: 28, hour: 18, minute: 0, second: 0, offset: TimeSpan.Zero);
        var active = preference.Window!.IsActive(now);

        // Act / Assert
        settings.DesiredPingRole(eligible: true, exempt: false, scheduleEnabled: preference.Enabled, active: active)
            .ShouldBe(new Optional<bool>(true));

        preference.Enabled.ShouldBeTrue();
        settings.Enabled = true;

        settings.DesiredPingRole(eligible: true, exempt: false, scheduleEnabled: preference.Enabled, active: active)
            .ShouldBe(new Optional<bool>(false));
    }

    [Test]
    public void DisableSchedule_ConfirmedLoss_RetainsWindowWithoutReactivatingOnReappointment()
    {
        // Arrange
        WorkingHoursPreference preference = new WorkingHoursPreference(guildId: 123, memberId: 456);
        preference.EnableSchedule(WorkingHoursTimeParser.Parse("22:00:00.123456789Z", "07:00:00Z", timezone: null));

        // Act
        preference.DisableSchedule();

        // Assert
        preference.Enabled.ShouldBeFalse();
        preference.Window!.StartText.ShouldBe("22:00:00.123456789");
        WorkingHoursSettings settings = new WorkingHoursSettings { Enabled = true };

        settings.DesiredPingRole(eligible: true, exempt: false, scheduleEnabled: preference.Enabled, active: false)
            .ShouldBe(new Optional<bool>(true));

        settings.DesiredPingRole(eligible: true, exempt: true, scheduleEnabled: preference.Enabled, active: false).HasValue.ShouldBeFalse();
    }
}