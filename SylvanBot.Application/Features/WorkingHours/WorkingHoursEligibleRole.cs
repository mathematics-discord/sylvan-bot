namespace SylvanBot.Application.Features.WorkingHours;

public sealed class WorkingHoursEligibleRole
{
    public ulong GuildId { get; set; }

    public ulong RoleId { get; set; }

    public bool IsExempt { get; set; }
}