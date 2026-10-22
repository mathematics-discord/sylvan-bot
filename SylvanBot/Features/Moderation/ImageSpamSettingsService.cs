namespace SylvanBot.Features.Moderation;

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Infrastructure.Data;

public sealed class ImageSpamSettingsService(
    BotDbContext dbContext,
    ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> settingsByGuild)
{
    private static ImageBurstSpamDetectorOptions ToOptions(ImageSpamSettings settings) =>
        new()
        {
            Window = settings.BurstDurationSeconds,
            DistinctChannelThreshold = settings.RequiredChannelCount,
            RequiredAttachmentCount = settings.RequiredAttachmentCount,
        };

    public async Task<Option<ImageBurstSpamDetectorOptions>> GetCurrentAsync(ulong guildId, CancellationToken ct)
    {
        var settings = await dbContext.ImageSpamSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.GuildId == guildId, ct)
            .ConfigureAwait(false);

        if (settings is not { IsEnabled: true })
        {
            settingsByGuild.TryRemove(guildId, out _);

            return new None();
        }

        var options = ToOptions(settings);
        settingsByGuild[guildId] = options;

        return new Some<ImageBurstSpamDetectorOptions>(options);
    }

    public async Task LoadEnabledSettingsAsync(CancellationToken ct)
    {
        List<ImageSpamSettings> settings = await dbContext.ImageSpamSettings
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var setting in settings)
            settingsByGuild[setting.GuildId] = ToOptions(setting);
    }

    public async Task<ImageBurstSpamDetectorOptions> UpsertAsync(
        ulong guildId,
        int? requiredChannelCount,
        int? requiredAttachmentCount,
        int? burstDurationSeconds,
        CancellationToken ct)
    {
        var settings = await dbContext.ImageSpamSettings
            .SingleOrDefaultAsync(x => x.GuildId == guildId, ct)
            .ConfigureAwait(false);

        if (settings is null)
        {
            settings = ImageSpamSettings.CreateDefault(guildId);
            dbContext.ImageSpamSettings.Add(settings);
        }

        settings.Update(requiredChannelCount, requiredAttachmentCount, burstDurationSeconds);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        var options = ToOptions(settings);
        settingsByGuild[guildId] = options;

        return options;
    }
}