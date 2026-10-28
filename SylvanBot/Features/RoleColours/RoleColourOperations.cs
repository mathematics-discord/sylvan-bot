using Microsoft.EntityFrameworkCore;
using SylvanBot.Infrastructure.Data;

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

    public async Task<Result<(RoleColourOption Option, bool Created)>> UpsertMappingAsync(
        ulong guildId,
        long? optionId,
        ulong sourceRoleId,
        ulong displayRoleId,
        CancellationToken ct)
    {
        if (optionId is <= 0)
            return new Result<(RoleColourOption Option, bool Created)>(
                OperationFailureException.Validation(description: "Mapping ID must be positive."));

        var option = optionId.HasValue
            ? await db.RoleColourOptions
                .SingleOrDefaultAsync(o => o.GuildId == guildId && o.OptionId == optionId.Value, cancellationToken: ct)
            : null;

        if (optionId.HasValue && option is null)
            return new Result<(RoleColourOption Option, bool Created)>(
                OperationFailureException.NotFound(description: $"Colour mapping {optionId.Value} was not found in this guild."));

        IQueryable<RoleColourOption> matchingRoles = db.RoleColourOptions
            .Where(mapping => mapping.GuildId == guildId)
            .Where(mapping => mapping.SourceRoleId == sourceRoleId && mapping.DisplayRoleId == displayRoleId);

        if (optionId is { } editingId)
            matchingRoles = matchingRoles
                .Where(mapping => mapping.OptionId != editingId);

        var duplicate = await matchingRoles
            .AnyAsync(ct);

        if (duplicate)
            return new Result<(RoleColourOption Option, bool Created)>(
                OperationFailureException.Conflict(description: "That source already grants this colour."));

        var created = option is null;

        if (option is null)
        {
            Result<RoleColourOption> result = RoleColourOption.Create(guildId: guildId, sourceRoleId: sourceRoleId, displayRoleId: displayRoleId);

            if (!result.IsSuccessful)
                return new Result<(RoleColourOption Option, bool Created)>(result.Error!);

            option = result.Value;
            db.RoleColourOptions.Add(option);
        }
        else
        {
            Result<ValueTuple> update = option.Update(sourceRoleId: sourceRoleId, displayRoleId: displayRoleId);

            if (!update.IsSuccessful)
                return new Result<(RoleColourOption Option, bool Created)>(update.Error!);
        }

        await db.SaveChangesAsync(ct);

        return (option, created);
    }

    public async Task<Result<RoleColourOption>> DeleteMappingAsync(ulong guildId, long optionId, CancellationToken ct)
    {
        if (optionId <= 0)
            return new Result<RoleColourOption>(OperationFailureException.Validation(description: "Mapping ID must be positive."));

        var option = await db.RoleColourOptions
            .SingleOrDefaultAsync(o => o.GuildId == guildId && o.OptionId == optionId, cancellationToken: ct);

        if (option is null)
            return new Result<RoleColourOption>(
                OperationFailureException.NotFound(description: $"Colour mapping {optionId} was not found in this guild."));

        db.RoleColourOptions.Remove(option);
        await db.SaveChangesAsync(ct);

        return option;
    }
}