namespace SylvanBot.Features.RoleColours;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Infrastructure.Data;

public sealed class RoleColourOperations(BotDbContext db)
{
    public async Task<ImmutableArray<RoleColourOption>> ListConfiguredOptionsAsync(ulong guildId, CancellationToken ct)
    {
        return (
            await db.RoleColourOptions.AsNoTracking().Where(option => option.GuildId == guildId)
                .OrderBy(option => option.OptionId).ToArrayAsync(ct)
        ).ToImmutableArray();
    }

    public async Task<(RoleColourOption Option, bool Created)> UpsertMappingAsync(
        ulong guildId,
        long? optionId,
        ulong sourceRoleId,
        ulong displayRoleId,
        CancellationToken ct
    )
    {
        if (optionId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(optionId), optionId, "Mapping ID must be positive.");

        var option = optionId.HasValue
            ? await db.RoleColourOptions.SingleOrDefaultAsync(o => o.GuildId == guildId && o.OptionId == optionId.Value,
                ct)
            : null;

        if (optionId.HasValue && option is null)
            throw new KeyNotFoundException($"Colour mapping {optionId.Value} was not found in this guild.");

        if (
            await db.RoleColourOptions.AnyAsync(
                o =>
                    o.GuildId == guildId
                    && o.SourceRoleId == sourceRoleId
                    && o.DisplayRoleId == displayRoleId
                    && (!optionId.HasValue || o.OptionId != optionId.Value),
                ct
            )
        )
            throw new ArgumentException("That source already grants this colour.", nameof(sourceRoleId));

        var created = option is null;
        if (option is null)
        {
            option = RoleColourOption.Create(guildId, sourceRoleId, displayRoleId);
            db.RoleColourOptions.Add(option);
        }
        else
        {
            option.Update(sourceRoleId, displayRoleId);
        }

        await db.SaveChangesAsync(ct);
        return (option, created);
    }

    public async Task<RoleColourOption> DeleteMappingAsync(ulong guildId, long optionId, CancellationToken ct)
    {
        if (optionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(optionId), optionId, "Mapping ID must be positive.");

        var option =
            await db.RoleColourOptions.SingleOrDefaultAsync(o => o.GuildId == guildId && o.OptionId == optionId, ct);
        if (option is null)
            throw new KeyNotFoundException($"Colour mapping {optionId} was not found in this guild.");

        db.RoleColourOptions.Remove(option);
        await db.SaveChangesAsync(ct);
        return option;
    }
}