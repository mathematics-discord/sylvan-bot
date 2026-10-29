using Discord;
using Discord.Interactions;
using SylvanBot.Features.RolePersistence;

namespace SylvanBot.Tests.Features.RolePersistence;

public sealed class PersistentRolePolicyTests
{
    private static IEnumerable<GuildPermission> StaffPermissions =>
        Enum.GetValues<GuildPermission>().Where(permission => ((ulong)permission & (ulong)PersistentRolePolicy.StaffPermissions) != 0);

    [TestCaseSource(nameof(StaffPermissions))]
    public void RejectsEveryStaffPermissionOnRole(GuildPermission permission) => PersistentRolePolicy.IsSafe((ulong)permission, []).ShouldBeFalse();

    [TestCaseSource(nameof(StaffPermissions))]
    public void RejectsEveryStaffPermissionInAnyChannel(GuildPermission permission) =>
        PersistentRolePolicy.IsSafe(0, [0, (ulong)permission, 0]).ShouldBeFalse();

    [Test]
    public void AllowsOrdinaryRolePermissions() =>
        PersistentRolePolicy
            .IsSafe((ulong)(GuildPermission.ViewChannel | GuildPermission.SendMessages), [(ulong)GuildPermission.AttachFiles])
            .ShouldBeTrue();

    [Test]
    public void PermissionEscalationInvalidatesPreviouslySafeRole()
    {
        PersistentRolePolicy.IsSafe(0, []).ShouldBeTrue();
        PersistentRolePolicy.IsSafe((ulong)GuildPermission.ManageRoles, []).ShouldBeFalse();
    }

    [Test]
    public void CommandsRequireAuditLogAtRegistrationAndRuntime()
    {
        var attributes = typeof(PersistenceModule).GetCustomAttributes(false);
        attributes.OfType<DefaultMemberPermissionsAttribute>().Single().Permissions.ShouldBe(GuildPermission.ViewAuditLog);
        attributes.OfType<RequireUserPermissionAttribute>().Single().GuildPermission.ShouldBe(GuildPermission.ViewAuditLog);
    }
}
