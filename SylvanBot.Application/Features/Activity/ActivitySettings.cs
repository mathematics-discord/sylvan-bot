namespace SylvanBot.Application.Features.Activity;

public sealed class ActivitySettings
{
    public ulong GuildId { get; set; }

    public ulong Role1Id { get; set; }

    public ulong Role2Id { get; set; }

    public long Role1Score { get; set; }

    public long Role2Score { get; set; }

    public long MaxDaily { get; set; }
}
