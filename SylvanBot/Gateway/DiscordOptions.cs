using System.ComponentModel.DataAnnotations;

namespace SylvanBot.Gateway;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public ulong[] ImageBurstSpamAllowlistedRoleIds { get; init; } = [];

    public ulong[] ImageBurstSpamAllowlistedUserIds { get; init; } = [];

    [Range(minimum: 0, maximum: 7)]
    public int ImageBurstSpamHistoryPruneDays { get; init; } = 1;

    [UsedImplicitly]
    [Range(minimum: 0, maximum: 50000)]
    public int MessageCacheSize { get; init; }

    [Required]
    public string Token { get; init; } = string.Empty;
}
