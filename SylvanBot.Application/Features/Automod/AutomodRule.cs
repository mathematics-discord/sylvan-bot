namespace SylvanBot.Application.Features.Automod;

public sealed class AutomodRule
{
    public long Id { get; init; }

    public ulong GuildId { get; init; }

    public string Pattern { get; init; } = string.Empty;

    public AutomodMatchType MatchType { get; init; }

    public AutomodAction Action { get; init; }

    public long? DurationSeconds { get; init; }
}