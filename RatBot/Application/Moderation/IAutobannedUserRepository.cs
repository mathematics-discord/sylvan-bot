using RatBot.Domain.Moderation;

namespace RatBot.Application.Moderation;

public interface IAutobannedUserRepository
{
    Task AddAsync(AutobannedUser user, CancellationToken ct = default);
    Task<AutobannedUser?> GetAsync(ulong guildId, ulong userId, CancellationToken ct = default);
}