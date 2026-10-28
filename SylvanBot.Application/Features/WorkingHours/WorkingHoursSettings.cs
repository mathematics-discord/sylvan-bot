namespace SylvanBot.Application.Features.WorkingHours;

public sealed class WorkingHoursSettings
{
    public ulong GuildId { get; set; }

    public bool Enabled { get; set; }

    public ulong? PingRoleId { get; set; }

    public ICollection<WorkingHoursEligibleRole> EligibleRoles { get; set; } = [];

    public bool IsComplete => PingRoleId.HasValue && EligibleRoles.Count > 0;

    public Result<ValueTuple> SetPingRole(ulong roleId)
    {
        if (Enabled)
            return new Result<ValueTuple>(OperationFailureException.Validation(description: "Disable Working Hours before changing the ping role."));

        var eligible = EligibleRoles.Any(role => role.RoleId == roleId);

        if (roleId == 0 || roleId == GuildId || eligible)
            return new Result<ValueTuple>(
                OperationFailureException.Validation(description: "The ping role cannot be @everyone, an eligible role or an exemption role."));

        PingRoleId = roleId;

        return new Result<ValueTuple>(default(ValueTuple));
    }

    public Result<ValueTuple> ChangeEligibleRole(ulong roleId, bool add)
    {
        if (roleId == 0 || roleId == GuildId || roleId == PingRoleId)
            return new Result<ValueTuple>(
                OperationFailureException.Validation(description: "An eligible role cannot be @everyone or the ping role."));

        var role = EligibleRoles.SingleOrDefault(item => item.RoleId == roleId);

        if (add)
        {
            if (role is null)
                EligibleRoles.Add(new WorkingHoursEligibleRole { GuildId = GuildId, RoleId = roleId });

            return new Result<ValueTuple>(default(ValueTuple));
        }

        if (role is null)
            return new Result<ValueTuple>(default(ValueTuple));

        if (role.IsExempt)
            return new Result<ValueTuple>(
                OperationFailureException.Validation(description: "Remove this role's exemption before removing its eligibility."));

        if (Enabled && EligibleRoles.Count == 1)
            return new Result<ValueTuple>(
                OperationFailureException.Validation(description: "Disable Working Hours before removing the last eligible role."));

        EligibleRoles.Remove(role);

        return new Result<ValueTuple>(default(ValueTuple));
    }

    public Result<ValueTuple> ChangeExemptRole(ulong roleId, bool add)
    {
        var role = EligibleRoles.SingleOrDefault(item => item.RoleId == roleId);

        if (role is null)
        {
            if (add)
                return new Result<ValueTuple>(OperationFailureException.Validation(description: "An exemption role must already be eligible."));

            return new Result<ValueTuple>(default(ValueTuple));
        }

        role.IsExempt = add;

        return new Result<ValueTuple>(default(ValueTuple));
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
