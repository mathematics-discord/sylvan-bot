namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailSettings
{
    public int Slot { get; set; } = 1;

    public ulong GuildId { get; set; }

    public ulong ForumId { get; set; }

    public ulong RoleId { get; set; }
}