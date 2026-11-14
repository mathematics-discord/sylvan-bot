namespace SylvanBot.Application.Features.Announcements;

public sealed class AnnouncementSettings
{
    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public ulong RoleId { get; set; }

    public ulong MessageId { get; set; }
}
