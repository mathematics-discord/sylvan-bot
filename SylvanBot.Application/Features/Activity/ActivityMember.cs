namespace SylvanBot.Application.Features.Activity;

public sealed class ActivityMember
{
    public ulong GuildId { get; set; }

    public ulong UserId { get; set; }

    public decimal Xp { get; set; }

    public bool OptedOut { get; set; }

    public DateTimeOffset ResetAt { get; set; }

    public ulong AssignedRoleId { get; set; }
}
