namespace SylvanBot.Application.Features.Logging;

/// <summary>A captured member event. A present empty name means unset; absence means unavailable.</summary>
public sealed record MemberLogEntry(
    ulong GuildId,
    ulong UserId,
    string Username,
    Optional<string> Name,
    DateTimeOffset OccurredAtUtc,
    MemberLogEvent Event,
    Optional<string> PreviousName = default)
{
    public static Optional<MemberLogEntry> NameChanged(
        ulong guildId,
        ulong userId,
        string username,
        Optional<string> before,
        string after,
        DateTimeOffset receivedAtUtc,
        MemberLogEvent eventKind)
        => !before.HasValue || string.Equals(before.Value, after, StringComparison.Ordinal)
            ? default
            : new Optional<MemberLogEntry>(
                new MemberLogEntry(
                    guildId,
                    userId,
                    username,
                    new Optional<string>(after),
                    receivedAtUtc,
                    eventKind,
                    before));
}