using System.ComponentModel.DataAnnotations;

namespace SylvanBot.Gateway;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public ulong[] ImageBurstSpamAllowlistedRoleIds { get; init; } = [];

    public ulong[] ImageBurstSpamAllowlistedUserIds { get; init; } = [];

    [Range(minimum: 0, maximum: 7)] public int ImageBurstSpamHistoryPruneDays { get; init; } = 1;

    [Range(minimum: 1000, maximum: 50000)] public int MessageCacheSize { get; init; } = 5000;

    [Required] public string Token { get; init; } = string.Empty;
}