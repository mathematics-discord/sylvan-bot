namespace RatBot.Gateway;

using System.ComponentModel.DataAnnotations;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public bool AlwaysDownloadUsers { get; init; } = true;

    public GatewayIntents GatewayIntents { get; init; } = GatewayIntents.Guilds
        | GatewayIntents.GuildMembers
        | GatewayIntents.GuildMessages
        | GatewayIntents.GuildMessageReactions
        | GatewayIntents.MessageContent;

    public ulong[] ImageBurstSpamAllowlistedRoleIds { get; init; } = [];

    public ulong[] ImageBurstSpamAllowlistedUserIds { get; init; } = [];

    [Range(0, 7)] public int ImageBurstSpamHistoryPruneDays { get; init; } = 1;

    [Range(1000, 50000)] public int MessageCacheSize { get; init; } = 5000;

    [Required] public string Token { get; init; } = string.Empty;
}
