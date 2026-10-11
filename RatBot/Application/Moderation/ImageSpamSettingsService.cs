using Microsoft.EntityFrameworkCore;
using RatBot.Domain.Moderation;
using RatBot.Infrastructure.Data;

namespace RatBot.Application.Moderation;

public sealed class ImageSpamSettingsService(
    BotDbContext dbContext,
    ImageBurstSpamDetectorSettings detectorSettings)
{
    private static ImageBurstSpamDetectorOptions ToOptions(ImageSpamSettings settings) =>
        new ImageBurstSpamDetectorOptions
        {
            Window = settings.BurstDurationSeconds,
            DistinctChannelThreshold = settings.RequiredChannelCount,
            RequiredAttachmentCount = settings.RequiredAttachmentCount,
        };

    public async Task<Option<ImageBurstSpamDetectorOptions>> GetCurrentAsync(ulong guildId, CancellationToken ct)
    {
        ImageSpamSettings? settings = await dbContext.ImageSpamSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.GuildId == guildId, ct)
            .ConfigureAwait(false);

        if (settings is not { IsEnabled: true })
        {
            detectorSettings.Remove(guildId);
            return new None();
        }

        ImageBurstSpamDetectorOptions options = ToOptions(settings);
        detectorSettings.Update(guildId, options);

        return new Some<ImageBurstSpamDetectorOptions>(options);
    }

    public async Task LoadEnabledSettingsAsync(CancellationToken ct)
    {
        List<ImageSpamSettings> settings = await dbContext.ImageSpamSettings
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);

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
        ImageSpamSettings? settings = await dbContext.ImageSpamSettings
            .SingleOrDefaultAsync(x => x.GuildId == guildId, ct)
            .ConfigureAwait(false);

        if (settings is null)
        {
            settings = ImageSpamSettings.CreateDefault(guildId);
            dbContext.ImageSpamSettings.Add(settings);
        }

        settings.Update(requiredChannelCount, requiredAttachmentCount, burstDurationSeconds);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        ImageBurstSpamDetectorOptions options = ToOptions(settings);
        detectorSettings.Update(guildId, options);

        return options;
    }
}