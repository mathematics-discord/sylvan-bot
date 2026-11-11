namespace SylvanBot.Application.Features.Activity;

public sealed class ActivityWeight
{
    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public decimal Weight { get; set; }
}
