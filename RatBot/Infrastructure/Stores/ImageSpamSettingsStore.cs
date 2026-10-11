using Microsoft.EntityFrameworkCore;
using RatBot.Application.Moderation;
using RatBot.Domain.Moderation;
using RatBot.Infrastructure.Data;

namespace RatBot.Infrastructure.Stores;

public sealed class ImageSpamSettingsStore(BotDbContext dbContext) : IImageSpamSettingsStore
{
    public Task<ImageSpamSettings?> GetAsync(ulong guildId, CancellationToken ct)
    {
        return dbContext.ImageSpamSettings.AsNoTracking().SingleOrDefaultAsync(x => x.GuildId == guildId, ct);
    }

    public async Task<IReadOnlyList<ImageSpamSettings>> ListEnabledAsync(CancellationToken ct)
    {
        return await dbContext.ImageSpamSettings.AsNoTracking().Where(x => x.IsEnabled).ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ImageSpamSettings> UpsertAsync(
        ulong guildId,
        int? requiredChannelCount,
        int? requiredAttachmentCount,
        int? burstDurationSeconds,
        CancellationToken ct
    )
    {
        ImageSpamSettings? settings = await dbContext.ImageSpamSettings
            .SingleOrDefaultAsync(x => x.GuildId == guildId, ct).ConfigureAwait(false);

        if (settings is null)
        {
            settings = ImageSpamSettings.CreateDefault(guildId);
            dbContext.ImageSpamSettings.Add(settings);
        }

        settings.Update(requiredChannelCount, requiredAttachmentCount, burstDurationSeconds);

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return settings;
    }
}