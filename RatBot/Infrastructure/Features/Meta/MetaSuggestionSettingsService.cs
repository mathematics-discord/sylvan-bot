using Microsoft.EntityFrameworkCore;
using RatBot.Application.Features.Meta;
using RatBot.Infrastructure.Data;

namespace RatBot.Infrastructure.Features.Meta;

public sealed class MetaSuggestionSettingsService(BotDbContext db, ILogger logger)
{
    private readonly BotDbContext _db = db;
    private readonly ILogger _logger = logger.ForContext<MetaSuggestionSettingsService>();

    public async Task<ErrorOr<MetaSuggestionSettings>> GetAsync(ulong guildId, CancellationToken ct = default)
    {
        MetaSuggestionSettings? setting = await _db.MetaSuggestionSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.GuildId == guildId, ct);

        return setting is null ? MetaProposalErrors.SettingsNotConfigured : setting;
    }

    public Task<ErrorOr<Success>> UpsertCabinetChairRoleAsync(ulong guildId, ulong roleId,
        CancellationToken ct = default)
    {
        return UpsertAsync(guildId, settings => settings.SetCabinetChairRole(roleId), ct);
    }

    public Task<ErrorOr<Success>> UpsertCabinetRoleAsync(ulong guildId, ulong roleId, CancellationToken ct = default)
    {
        return UpsertAsync(guildId, settings => settings.SetCabinetRole(roleId), ct);
    }

    public Task<ErrorOr<Success>> UpsertCommitteeRoleAsync(ulong guildId, ulong roleId, CancellationToken ct = default)
    {
        return UpsertAsync(guildId, settings => settings.SetCommitteeRole(roleId), ct);
    }

    public Task<ErrorOr<Success>> UpsertProposalsForumChannelAsync(ulong guildId, ulong forumChannelId,
        CancellationToken ct = default)
    {
        return UpsertAsync(guildId, settings => settings.SetProposalsForum(forumChannelId), ct);
    }

    public Task<ErrorOr<Success>> UpsertSuggestionsForumChannelAsync(ulong guildId, ulong forumChannelId,
        CancellationToken ct = default)
    {
        return UpsertAsync(guildId, settings => settings.SetSuggestionsForum(forumChannelId), ct);
    }

    private async Task<ErrorOr<Success>> UpsertAsync(ulong guildId,
        Func<MetaSuggestionSettings, ErrorOr<Success>> update, CancellationToken ct)
    {
        MetaSuggestionSettings? existing =
            await _db.MetaSuggestionSettings.SingleOrDefaultAsync(x => x.GuildId == guildId, ct);

        MetaSuggestionSettings settings = existing ?? MetaSuggestionSettings.Create(guildId);

        if (existing is null)
            _db.MetaSuggestionSettings.Add(settings);

        ErrorOr<Success> result = update(settings);

        if (result.IsError)
            return result.Errors;

        await _db.SaveChangesAsync(ct);

        _logger.Information("Meta proposal settings updated for guild {GuildId}", guildId);

        return Result.Success;
    }
}