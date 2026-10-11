using RatBot.Domain.Moderation;

namespace RatBot.Application.Moderation;

public interface IModerationService
{
    Task<AutobannedUser?> GetAutobanAsync(ulong guildId, ulong userId, CancellationToken ct = default);

    Task<ErrorOr<AutobannedUser>> RegisterAutobanAsync(ulong guildId, ulong userId, ulong modId,
        CancellationToken ct = default);
}