namespace SylvanBot.Features.SylvanMail;

public sealed class SylvanMailOptions
{
    public const string SectionName = "SylvanMail";

    public string Token { get; init; } = string.Empty;

    public ulong BotId { get; init; }
}
