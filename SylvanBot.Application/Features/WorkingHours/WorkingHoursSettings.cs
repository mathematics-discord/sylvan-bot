namespace SylvanBot.Application.Features.WorkingHours;

public sealed class WorkingHoursSettings
{
    public ulong GuildId { get; set; }

    public bool Enabled { get; set; }

    public ulong? PingRoleId { get; set; }

    public ICollection<WorkingHoursEligibleRole> EligibleRoles { get; set; } = [];

    public bool IsComplete => PingRoleId.HasValue && EligibleRoles.Count > 0;

    public ErrorOr<Success> SetPingRole(ulong roleId)
    {
        if (Enabled)
            return Error.Validation(description: "Disable Working Hours before changing the ping role.");

        var eligible = EligibleRoles
           .Any(role => role.RoleId == roleId);

        if (roleId == 0 || roleId == GuildId || eligible)
            return Error.Validation(description: "The ping role cannot be @everyone, an eligible role or an exemption role.");

        PingRoleId = roleId;

        return Result.Success;
    }

    public ErrorOr<Success> ChangeEligibleRole(ulong roleId, bool add)
    {
        if (roleId == 0 || roleId == GuildId || roleId == PingRoleId)
            return Error.Validation(description: "An eligible role cannot be @everyone or the ping role.");

        var role = EligibleRoles
           .SingleOrDefault(item => item.RoleId == roleId);

        if (add)
        {
            if (role is null)
                EligibleRoles.Add(new WorkingHoursEligibleRole { GuildId = GuildId, RoleId = roleId });

            return Result.Success;
        }

        if (role is null)
            return Result.Success;

        if (role.IsExempt)
            return Error.Validation(description: "Remove this role's exemption before removing its eligibility.");

        if (Enabled && EligibleRoles.Count == 1)
            return Error.Validation(description: "Disable Working Hours before removing the last eligible role.");

        EligibleRoles.Remove(role);

        return Result.Success;
    }

    public ErrorOr<Success> ChangeExemptRole(ulong roleId, bool add)
    {
        var role = EligibleRoles
           .SingleOrDefault(item => item.RoleId == roleId);

        if (role is null)
        {
            if (add)
                return Error.Validation(description: "An exemption role must already be eligible.");

            return Result.Success;
        }

        role.IsExempt = add;

        return Result.Success;
    }

    public bool? DesiredPingRole(bool eligible, bool exempt, bool scheduleEnabled, bool active)
    {
        if (!eligible)
            return false;

        if (!Enabled)
            return exempt ? null : true;

        if (scheduleEnabled)
            return active;

        return exempt ? null : true;
    }
}