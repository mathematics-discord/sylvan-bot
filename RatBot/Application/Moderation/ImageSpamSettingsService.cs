using RatBot.Domain.Moderation;

namespace RatBot.Application.Moderation;

public sealed class ImageSpamSettingsService(
    IImageSpamSettingsStore store,
    ImageBurstSpamDetectorSettings detectorSettings)
{
    private static ImageBurstSpamDetectorOptions ToOptions(ImageSpamSettings settings)
    {
        return new ImageBurstSpamDetectorOptions
        {
            Window = settings.BurstDurationSeconds,
            DistinctChannelThreshold = settings.RequiredChannelCount,
            RequiredAttachmentCount = settings.RequiredAttachmentCount
        };
    }

    public async Task<ImageBurstSpamDetectorOptions?> GetCurrentAsync(ulong guildId, CancellationToken ct)
    {
        ImageSpamSettings? settings = await store.GetAsync(guildId, ct).ConfigureAwait(false);

        if (settings is not { IsEnabled: true })
        {
            detectorSettings.Remove(guildId);
            return null;
        }

        ImageBurstSpamDetectorOptions options = ToOptions(settings);
        detectorSettings.Update(guildId, options);

        return options;
    }

    public async Task LoadEnabledSettingsAsync(CancellationToken ct)
    {
        IReadOnlyList<ImageSpamSettings> settings = await store.ListEnabledAsync(ct).ConfigureAwait(false);

        foreach (ImageSpamSettings setting in settings)
            detectorSettings.Update(setting.GuildId, ToOptions(setting));
    }

    public async Task<ImageBurstSpamDetectorOptions> UpsertAsync(
        ulong guildId,
        int? requiredChannelCount,
        int? requiredAttachmentCount,
        int? burstDurationSeconds,
        CancellationToken ct
    )
    {
        ImageSpamSettings settings = await store
            .UpsertAsync(guildId, requiredChannelCount, requiredAttachmentCount, burstDurationSeconds, ct)
            .ConfigureAwait(false);

        ImageBurstSpamDetectorOptions options = ToOptions(settings);
        detectorSettings.Update(guildId, options);

        return options;
    }
}