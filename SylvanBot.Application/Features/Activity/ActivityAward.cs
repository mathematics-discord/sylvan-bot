namespace SylvanBot.Application.Features.Activity;

public sealed class ActivityAward
{
    public ulong GuildId { get; set; }

    public ulong UserId { get; set; }

    public ulong MessageId { get; set; }

    public DateTimeOffset AwardedAt { get; set; }

    public decimal Xp { get; set; }
}
