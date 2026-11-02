namespace SylvanBot.Application.Features.Automod;

public sealed class AutomodSettings
{
    public ulong GuildId { get; set; }

    public AutomodAction Action { get; set; }

    public bool Enabled { get; set; } = true;
}
