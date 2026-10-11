using RatBot.Domain.Moderation;

namespace RatBot.Application.Moderation;

public sealed class ModerationService(IAutobannedUserRepository autobannedUsers, ILogger logger) : IModerationService
{
    private readonly ILogger _logger = logger.ForContext<ModerationService>();

    public Task<AutobannedUser?> GetAutobanAsync(ulong guildId, ulong userId, CancellationToken ct = default) =>
        autobannedUsers.GetAsync(guildId, userId, ct);

    public async Task<ErrorOr<AutobannedUser>> RegisterAutobanAsync(ulong guildId, ulong userId, ulong modId, CancellationToken ct = default)
    {
        AutobannedUser? existing = await autobannedUsers.GetAsync(guildId, userId, ct);

        if (existing is not null)
            return ModerationErrors.UserAlreadyAutobanned(userId);

        AutobannedUser autobannedUser = AutobannedUser.Create(guildId, userId, modId, DateTimeOffset.UtcNow);

        await autobannedUsers.AddAsync(autobannedUser, ct);

        _logger.Information("Registered user {BannedUserId} for autoban in guild {GuildId} by moderator {ModeratorId}", userId, guildId, modId);

        return autobannedUser;
    }
}
