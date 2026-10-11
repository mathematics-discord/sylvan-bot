using RatBot.Domain.Moderation;

namespace RatBot.Application.Moderation;

public interface IImageSpamSettingsStore
{
    Task<ImageSpamSettings?> GetAsync(ulong guildId, CancellationToken ct);

    Task<IReadOnlyList<ImageSpamSettings>> ListEnabledAsync(CancellationToken ct);

    Task<ImageSpamSettings> UpsertAsync(
        ulong guildId,
        int? requiredChannelCount,
        int? requiredAttachmentCount,
        int? burstDurationSeconds,
        CancellationToken ct
    );
}