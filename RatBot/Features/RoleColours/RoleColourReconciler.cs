using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Features.RoleColours;

// Live Discord roles are the equipped state. Colour writes are serialized per guild.
public sealed class RoleColourReconciler(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger logger,
    DiscordSocketClient discordClient)
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _guildGates =
        new ConcurrentDictionary<ulong, SemaphoreSlim>();

    private readonly ILogger _logger = logger.ForContext<RoleColourReconciler>();

    public async Task<ImmutableArray<IRole>> ListEligibleColoursAsync(IGuild guild, ulong userId, CancellationToken ct)
    {
        IGuildUser? member = await ReadMemberAsync(guild, userId, new RequestOptions { CancelToken = ct });

        return member is null ? [] : EligibleColours(guild, member.RoleIds, await ReadMappingsAsync(guild.Id, ct));
    }

    internal static ErrorOr<Success> ValidateMapping(IGuild guild, ulong sourceId, ulong colourId)
    {
        IRole? source = guild.GetRole(sourceId);
        IRole? colour = guild.GetRole(colourId);

        if (source is null || source.Guild.Id != guild.Id)
            return Error.Validation(description: "Source role does not exist in this guild.");

        if (colour is null || colour.Guild.Id != guild.Id)
            return Error.Validation(description: "Colour role does not exist in this guild.");

        if (sourceId == colourId)
            return Error.Validation(description: "Source and colour roles must differ.");

        if (source.Colors.PrimaryColor != Color.Default)
            return Error.Validation(description: "Source role must not have a colour set.");

        if (colour.Colors.PrimaryColor == Color.Default)
            return Error.Validation(description: "Colour role must have a colour set.");

        return Result.Success;
    }

    private static ImmutableArray<IRole> EligibleColours(IGuild guild,
        IReadOnlyCollection<ulong> roles,
        ImmutableArray<RoleColourOption> mappings)
    {
        return
        [
            .. HeldGrants(guild.Id, roles, mappings)
                .Where(o => !ValidateMapping(guild, o.SourceRoleId, o.DisplayRoleId).IsError)
                .Select(o => guild.GetRole(o.DisplayRoleId))
                .DistinctBy(role => role.Id)
                .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(role => role.Id)
        ];
    }

    private static bool HoldsSource(ulong guildId, IReadOnlyCollection<ulong> roles, ulong sourceId)
    {
        return sourceId == guildId || roles.Contains(sourceId);
    }

    private static IEnumerable<RoleColourOption> HeldGrants(
        ulong guildId,
        IReadOnlyCollection<ulong> roles,
        ImmutableArray<RoleColourOption> mappings
    )
    {
        return mappings.Where(o => HoldsSource(guildId, roles, o.SourceRoleId));
    }

    public async Task<ErrorOr<ulong?>> TrySetUserColourAsync(IGuild guild,
        ulong userId,
        ulong? colourId,
        CancellationToken ct)
    {
        SemaphoreSlim gate = _guildGates.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            return await SetUserColourAsync(guild, userId, colourId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Role colour update incomplete for guild {GuildId}, member {MemberId}", guild.Id, userId);

            return Error.Failure(
                description:
                "Discord could not complete the colour update. Your roles may have changed; please try again.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ErrorOr<ulong?>> SetUserColourAsync(IGuild guild,
        ulong userId,
        ulong? colourId,
        CancellationToken ct)
    {
        RequestOptions request = new RequestOptions { CancelToken = ct };
        IGuildUser? member = await ReadMemberAsync(guild, userId, request);

        if (member is null)
            return Error.NotFound(description: "The member is no longer in this guild.");

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guild.Id, ct);

        if (colourId.HasValue)
        {
            ImmutableArray<IRole> eligible = EligibleColours(guild, member.RoleIds, mappings);

            if (!eligible.Any(role => role.Id == colourId.Value))
                return Error.Validation(description: "That colour is no longer available to you.");
        }

        HashSet<ulong> worn = ManagedDisplayRoles(member.RoleIds, mappings);
        HashSet<ulong> remove = worn.Where(id => id != colourId).ToHashSet();
        bool addColour = colourId.HasValue && !worn.Contains(colourId.Value);

        if (remove.Count == 0 && !addColour)
            return SoleColour(worn);

        await RemoveColoursAsync(member, remove, request);

        if (addColour)
            await member.AddRoleAsync(colourId!.Value, request);

        IGuildUser? actual = await ReadMemberAsync(guild, userId, request);

        if (actual is null)
            return RolesChangedError();

        HashSet<ulong> actualColours = ManagedDisplayRoles(actual.RoleIds, mappings);

        if (colourId.HasValue)
        {
            if (actualColours.Count != 1 || !actualColours.Contains(colourId.Value))
                return RolesChangedError();
        }
        else if (actualColours.Count != 0)
        {
            return RolesChangedError();
        }

        return SoleColour(actualColours);
    }

    public async Task<ErrorOr<ulong?>> ReconcileUserRolesAsync(
        IGuild guild,
        ulong userId,
        CancellationToken ct,
        IReadOnlyCollection<ulong>? previousRoleIds = null,
        ImmutableArray<RoleColourOption> previousMappings = default
    )
    {
        SemaphoreSlim gate = _guildGates.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            return await ProcessRoleColourReconciliation(guild, userId, previousRoleIds, previousMappings, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Role colour update incomplete for guild {GuildId}, member {MemberId}", guild.Id, userId);

            return Error.Failure(
                description:
                "Discord could not complete the colour update. Your roles may have changed; please try again.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ErrorOr<ulong?>> ProcessRoleColourReconciliation(
        IGuild guild,
        ulong userId,
        IReadOnlyCollection<ulong>? previousRoleIds,
        ImmutableArray<RoleColourOption> previousMappings,
        CancellationToken ct
    )
    {
        RequestOptions request = new RequestOptions { CancelToken = ct };
        IGuildUser? member = await ReadMemberAsync(guild, userId, request);

        if (member is null)
            return Error.NotFound(description: "The member is no longer in this guild.");

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guild.Id, ct);
        ImmutableArray<RoleColourOption> old = previousMappings.IsDefault ? mappings : previousMappings;
        HashSet<ulong> worn = ManagedDisplayRoles(member.RoleIds, mappings.Concat(old));
        HashSet<ulong> remove;

        if (HeldGrants(guild.Id, member.RoleIds, mappings).Any())
        {
            ulong? currentColour = SoleColour(worn);
            ImmutableArray<IRole> eligible = EligibleColours(guild, member.RoleIds, mappings);

            if (eligible.Any(role => role.Id == currentColour))
                return currentColour;

            remove = worn;
        }
        else
        {
            IReadOnlyCollection<ulong> before = previousRoleIds ?? member.RoleIds;
            HashSet<ulong> previousColours = HeldGrants(guild.Id, before, old).Select(o => o.DisplayRoleId).ToHashSet();

            remove = [.. worn.Intersect(previousColours)];
        }

        if (remove.Count == 0)
            return SoleColour(worn);

        await RemoveColoursAsync(member, remove, request);
        IGuildUser? actual = await ReadMemberAsync(guild, userId, request);

        if (actual is null)
            return RolesChangedError();

        HashSet<ulong> actualColours = ManagedDisplayRoles(actual.RoleIds, mappings.Concat(old));

        if (actualColours.Overlaps(remove))
            return RolesChangedError();

        return SoleColour(actualColours);
    }

    // Old mappings are a temporary snapshot from the admin command, never persisted.
    public async Task<int> ReconcileGuildAsync(IGuild guild,
        CancellationToken ct,
        ImmutableArray<RoleColourOption> previousMappings = default)
    {
        try
        {
            ImmutableArray<RoleColourOption> current = await ReadMappingsAsync(guild.Id, ct);
            ImmutableArray<RoleColourOption> old = previousMappings.IsDefault ? [] : previousMappings;

            if (current.IsEmpty && old.IsEmpty)
                return 0;

            if (guild is SocketGuild { HasAllMembers: false })
                await guild.DownloadUsersAsync().WaitAsync(ct);

            if (guild is SocketGuild { HasAllMembers: false })
                throw new InvalidOperationException("Guild member cache is incomplete.");

            IReadOnlyCollection<IGuildUser> members = await guild.GetUsersAsync(
                CacheMode.AllowDownload,
                new RequestOptions { CancelToken = ct });

            int incomplete = 0;

            foreach (IGuildUser member in members)
            {
                bool participates = HeldGrants(guild.Id, member.RoleIds, current).Any();
                bool participated = HeldGrants(guild.Id, member.RoleIds, old).Any();

                if (!participates && !participated)
                    continue;

                ErrorOr<ulong?> result = await ReconcileUserRolesAsync(guild, member.Id, ct, member.RoleIds, old);

                if (result.IsError)
                    incomplete++;
            }

            return incomplete;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Role colour guild check incomplete for guild {GuildId}", guild.Id);
            return 1;
        }
    }

    private async Task<ImmutableArray<RoleColourOption>> ReadMappingsAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        return [.. await db.RoleColourOptions.AsNoTracking().Where(o => o.GuildId == guildId).ToArrayAsync(ct)];
    }

    private static async Task RemoveColoursAsync(IGuildUser member, IEnumerable<ulong> colours, RequestOptions request)
    {
        foreach (ulong roleId in colours)
            await member.RemoveRoleAsync(roleId, request);
    }

    private static ulong? SoleColour(HashSet<ulong> colours)
    {
        return colours.Count == 1 ? colours.Single() : null;
    }

    private static Error RolesChangedError()
    {
        return Error.Failure(
            description: "Discord roles changed during the update. Please check your roles and try again.");
    }

    private async Task<IGuildUser?> ReadMemberAsync(IGuild guild, ulong userId, RequestOptions request)
    {
        return guild is SocketGuild
            ? await discordClient.Rest.GetGuildUserAsync(guild.Id, userId, request)
            : await guild.GetUserAsync(userId, CacheMode.AllowDownload, request);
    }

    private static HashSet<ulong> ManagedDisplayRoles(IReadOnlyCollection<ulong> roles,
        IEnumerable<RoleColourOption> mappings)
    {
        HashSet<ulong> displayRoleIds = mappings.Select(mapping => mapping.DisplayRoleId).ToHashSet();
        return roles.Where(displayRoleIds.Contains).ToHashSet();
    }
}