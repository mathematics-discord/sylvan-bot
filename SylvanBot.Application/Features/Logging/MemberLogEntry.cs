namespace SylvanBot.Application.Features.Logging;

/// <summary>A captured member event. A present empty nickname means unset; absence means unavailable.</summary>
public sealed record MemberLogEntry(
    ulong GuildId,
    ulong UserId,
    string Username,
    Optional<string> Nickname,
    DateTimeOffset OccurredAtUtc,
    MemberLogEvent Event,
    Optional<string> PreviousNickname = default)
{
    public static Optional<MemberLogEntry> NicknameChanged(
        ulong guildId,
        ulong userId,
        string username,
        Optional<string> before,
        string after,
        DateTimeOffset receivedAtUtc)
        => !before.HasValue || string.Equals(before.Value, after, StringComparison.Ordinal)
            ? default
            : new Optional<MemberLogEntry>(
                new MemberLogEntry(
                    guildId,
                    userId,
                    username,
                    new Optional<string>(after),
                    receivedAtUtc,
                    MemberLogEvent.NicknameChange,
                    before));
}