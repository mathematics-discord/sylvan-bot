using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RoleColours;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.RoleColours;

public sealed class RoleColourOperations(BotDbContext db)
{
    public async Task<ImmutableArray<RoleColourOption>> ListConfiguredOptionsAsync(ulong guildId, CancellationToken ct)
    {
        RoleColourOption[] options = await db.RoleColourOptions
            .AsNoTracking()
            .Where(option => option.GuildId == guildId)
            .OrderBy(option => option.OptionId)
            .ToArrayAsync(ct);

        return options
            .ToImmutableArray();
    }

    public async Task<(RoleColourOption Option, bool Created)> UpsertMappingAsync(
        ulong guildId,
        long? optionId,
        ulong sourceRoleId,
        ulong displayRoleId,
        CancellationToken ct)
    {
        if (optionId is <= 0)
            throw new BotCommandException("Mapping ID must be positive.");

        RoleColourOption? option = optionId.HasValue
            ? await db.RoleColourOptions
                .SingleOrDefaultAsync(o => o.GuildId == guildId && o.OptionId == optionId.Value, cancellationToken: ct)
            : null;

        if (optionId.HasValue && option is null)
            throw new BotCommandException($"Colour mapping {optionId.Value} was not found in this guild.");

        IQueryable<RoleColourOption> matchingRoles = db.RoleColourOptions
            .Where(mapping => mapping.GuildId == guildId)
            .Where(mapping => mapping.SourceRoleId == sourceRoleId && mapping.DisplayRoleId == displayRoleId);

        if (optionId is { } editingId)

            matchingRoles = matchingRoles
                .Where(mapping => mapping.OptionId != editingId);

        var duplicate = await matchingRoles
            .AnyAsync(ct);

        if (duplicate)
            throw new BotCommandException("That source already grants this colour.");

        var created = option is null;

        if (option is null)
        {
            option = RoleColourOption.Create(guildId: guildId, sourceRoleId: sourceRoleId, displayRoleId: displayRoleId);

            db.RoleColourOptions.Add(option);
        }
        else
            option.Update(sourceRoleId: sourceRoleId, displayRoleId: displayRoleId);
        await db.SaveChangesAsync(ct);

        return (option, created);
    }

    public async Task<RoleColourOption> DeleteMappingAsync(ulong guildId, long optionId, CancellationToken ct)
    {
        if (optionId <= 0)
            throw new BotCommandException("Mapping ID must be positive.");

        RoleColourOption? option = await db.RoleColourOptions
            .SingleOrDefaultAsync(o => o.GuildId == guildId && o.OptionId == optionId, cancellationToken: ct);

        if (option is null)
            throw new BotCommandException($"Colour mapping {optionId} was not found in this guild.");

        db.RoleColourOptions.Remove(option);
        await db.SaveChangesAsync(ct);

        return option;
    }
}