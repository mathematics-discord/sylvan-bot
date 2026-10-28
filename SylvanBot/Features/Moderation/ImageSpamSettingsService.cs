using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.Moderation;

using DotNext;

public sealed class ImageSpamSettingsService(IDbContextFactory<BotDbContext> contexts) : IDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    private readonly ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions> _settings =
        new ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>();

    public void Dispose() => _gate.Dispose();

    public Optional<ImageBurstSpamDetectorOptions> Current(ulong guildId) =>
        _settings.TryGetValue(guildId, out var settings) ? new Optional<ImageBurstSpamDetectorOptions>(settings) : default;

    public async Task<Optional<ImageBurstSpamDetectorOptions>> GetCurrentAsync(ulong guildId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await using var dbContext = await contexts.CreateDbContextAsync(ct);

            var settings = await dbContext.ImageSpamSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.GuildId == guildId, cancellationToken: ct);

            if (settings is not { IsEnabled: true })
            {
                _settings.TryRemove(key: guildId, value: out _);

                return default;
            }

            var options = ToOptions(settings);
            _settings[guildId] = options;

            return new Optional<ImageBurstSpamDetectorOptions>(options);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LoadEnabledSettingsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await using var dbContext = await contexts.CreateDbContextAsync(ct);

            List<ImageSpamSettings> settings = await dbContext.ImageSpamSettings
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .ToListAsync(ct);

            foreach (var setting in settings)
                _settings[setting.GuildId] = ToOptions(setting);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImageBurstSpamDetectorOptions> UpsertAsync(
        ulong guildId,
        int? requiredChannelCount,
        int? requiredAttachmentCount,
        int? burstDurationSeconds,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await using var dbContext = await contexts.CreateDbContextAsync(ct);

            var settings = await dbContext.ImageSpamSettings
                .SingleOrDefaultAsync(x => x.GuildId == guildId, cancellationToken: ct);

            if (settings is null)
            {
                settings = ImageSpamSettings.CreateDefault(guildId);
                dbContext.ImageSpamSettings.Add(settings);
            }

            settings.Update(
                requiredChannelCount: requiredChannelCount,
                requiredAttachmentCount: requiredAttachmentCount,
                burstDurationSeconds: burstDurationSeconds);
            await dbContext.SaveChangesAsync(ct);

            var options = ToOptions(settings);
            _settings[guildId] = options;

            return options;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ImageBurstSpamDetectorOptions ToOptions(ImageSpamSettings settings) =>
        new ImageBurstSpamDetectorOptions()
        {
            Window = settings.BurstDurationSeconds,
            DistinctChannelThreshold = settings.RequiredChannelCount,
            RequiredAttachmentCount = settings.RequiredAttachmentCount,
        };
}