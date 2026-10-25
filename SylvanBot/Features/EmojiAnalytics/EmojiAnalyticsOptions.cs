namespace SylvanBot.Features.EmojiAnalytics;

public sealed class EmojiAnalyticsOptions
{
    public const string SectionName = "EmojiAnalytics";

    public ulong[] EnabledGuildIds { get; init; } = [];

    public bool IsEnabled(ulong guildId)
    {
        return EnabledGuildIds.Contains(guildId);
    }
}