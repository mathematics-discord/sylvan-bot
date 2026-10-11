namespace RatBot.Domain.Moderation;

public sealed record AutobannedUser
{
    private AutobannedUser()
    {
    }

    public ulong BannedUser { get; private init; }

    public ulong GuildId { get; private init; }
    public ulong Moderator { get; private init; }
    public DateTimeOffset RegisteredAtUtc { get; private init; }

    public static AutobannedUser Create(ulong guildId, ulong bannedUser, ulong moderator,
        DateTimeOffset registeredAtUtc)
    {
        return new AutobannedUser
        {
            GuildId = guildId,
            BannedUser = bannedUser,
            Moderator = moderator,
            RegisteredAtUtc = registeredAtUtc
        };
    }
}