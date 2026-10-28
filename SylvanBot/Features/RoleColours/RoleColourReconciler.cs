using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RoleColours;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.RoleColours;

using DotNext;

// Live Discord roles are the equipped state. Colour writes are serialized per guild.
public sealed class RoleColourReconciler(IDbContextFactory<BotDbContext> dbContextFactory, ILogger logger, DiscordSocketClient discordClient)
    : IDisposable
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _guildGates = new ConcurrentDictionary<ulong, SemaphoreSlim>();

    private readonly ILogger _logger = logger.ForContext<RoleColourReconciler>();

    public void Dispose()
    {
        foreach (var gate in _guildGates.Values)
            gate.Dispose();
    }

    public async Task<ImmutableArray<IRole>> ListEligibleColoursAsync(IGuild guild, ulong userId, CancellationToken ct)
    {
        Optional<IGuildUser> memberResult = await ReadMemberAsync(guild: guild, userId: userId, new RequestOptions { CancelToken = ct });

        return memberResult is { HasValue: true, Value: var member }
            ? EligibleColours(guild: guild, roles: member.RoleIds, await ReadMappingsAsync(guildId: guild.Id, ct: ct))
            : [];
    }

    public async Task<Result<Optional<ulong>>> TrySetUserColourAsync(IGuild guild, ulong userId, ulong? colourId, CancellationToken ct)
    {
        var gate = _guildGates.GetOrAdd(key: guild.Id, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
        await gate.WaitAsync(ct);

        try
        {
            return await SetUserColourAsync(guild: guild, userId: userId, colourId: colourId, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                exception: ex,
                "Role colour update incomplete for guild {GuildId}, member {MemberId}",
                propertyValue0: guild.Id,
                propertyValue1: userId);

            return new Result<Optional<ulong>>(
                OperationFailureException.Failure(description: "Discord could not complete the colour update. Your roles may have changed; please try again."));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<Optional<ulong>>> ReconcileUserRolesAsync(
        IGuild guild,
        ulong userId,
        CancellationToken ct,
        Optional<ImmutableArray<ulong>> previousRoleIds,
        Optional<ImmutableArray<RoleColourOption>> previousMappings)
    {
        var gate = _guildGates.GetOrAdd(key: guild.Id, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
        await gate.WaitAsync(ct);

        try
        {
            return await ProcessRoleColourReconciliation(
                guild: guild,
                userId: userId,
                previousRoleIds: previousRoleIds,
                previousMappings: previousMappings,
                ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                exception: ex,
                "Role colour update incomplete for guild {GuildId}, member {MemberId}",
                propertyValue0: guild.Id,
                propertyValue1: userId);

            return new Result<Optional<ulong>>(
                OperationFailureException.Failure(description: "Discord could not complete the colour update. Your roles may have changed; please try again."));
        }
        finally
        {
            gate.Release();
        }
    }

    // Old mappings are a temporary snapshot from the admin command, never persisted.
    public async Task<int> ReconcileGuildAsync(IGuild guild, CancellationToken ct, Optional<ImmutableArray<RoleColourOption>> previousMappings)
    {
        try
        {
            ImmutableArray<RoleColourOption> current = await ReadMappingsAsync(guildId: guild.Id, ct: ct);
            ImmutableArray<RoleColourOption> old = previousMappings is { HasValue: true, Value: var supplied } ? supplied : [];

            if (current.IsEmpty && old.IsEmpty)
                return 0;

            if (guild is SocketGuild { HasAllMembers: false })
                await guild.DownloadUsersAsync().WaitAsync(ct);

            if (guild is SocketGuild { HasAllMembers: false })
                throw new InvalidOperationException("Guild member cache is incomplete.");

            IReadOnlyCollection<IGuildUser> members = await guild.GetUsersAsync(
                mode: CacheMode.AllowDownload,
                new RequestOptions { CancelToken = ct });
            var incomplete = 0;

            foreach (var member in members)
            {
                var participates = HeldGrants(guildId: guild.Id, roles: member.RoleIds, mappings: current)
                    .Any();

                var participated = HeldGrants(guildId: guild.Id, roles: member.RoleIds, mappings: old)
                    .Any();

                if (!participates && !participated)
                    continue;

                Result<Optional<ulong>> result = await ReconcileUserRolesAsync(
                    guild: guild,
                    userId: member.Id,
                    ct: ct,
                    previousRoleIds: new Optional<ImmutableArray<ulong>>(
                        member.RoleIds
                            .ToImmutableArray()),
                    previousMappings: new Optional<ImmutableArray<RoleColourOption>>(old));

                if (!result.IsSuccessful)
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
            _logger.Error(exception: ex, "Role colour guild check incomplete for guild {GuildId}", propertyValue: guild.Id);

            return 1;
        }
    }

    internal static Result<ValueTuple> ValidateMapping(IGuild guild, ulong sourceId, ulong colourId)
    {
        var source = guild.GetRole(sourceId);
        var colour = guild.GetRole(colourId);

        if (source is null || source.Guild.Id != guild.Id)
            return new Result<ValueTuple>(OperationFailureException.Validation(description: "Source role does not exist in this guild."));

        if (colour is null || colour.Guild.Id != guild.Id)
            return new Result<ValueTuple>(OperationFailureException.Validation(description: "Colour role does not exist in this guild."));

        if (sourceId == colourId)
            return new Result<ValueTuple>(OperationFailureException.Validation(description: "Source and colour roles must differ."));

        if (source.Colors.PrimaryColor != Color.Default)
            return new Result<ValueTuple>(OperationFailureException.Validation(description: "Source role must not have a colour set."));

        if (colour.Colors.PrimaryColor == Color.Default)
            return new Result<ValueTuple>(OperationFailureException.Validation(description: "Colour role must have a colour set."));

        return new Result<ValueTuple>(default(ValueTuple));
    }

    private static ImmutableArray<IRole> EligibleColours(IGuild guild, IReadOnlyCollection<ulong> roles, ImmutableArray<RoleColourOption> mappings) =>
    [
        .. HeldGrants(guildId: guild.Id, roles: roles, mappings: mappings)
            .Where(o => ValidateMapping(guild: guild, sourceId: o.SourceRoleId, colourId: o.DisplayRoleId).IsSuccessful)
            .Select(o => guild.GetRole(o.DisplayRoleId))
            .DistinctBy(role => role.Id)
            .OrderBy(role => role.Name, comparer: StringComparer.OrdinalIgnoreCase)
            .ThenBy(role => role.Id),
    ];

    private static bool HoldsSource(ulong guildId, IReadOnlyCollection<ulong> roles, ulong sourceId) =>
        sourceId == guildId || roles.Contains(sourceId);

    private static IEnumerable<RoleColourOption> HeldGrants(
        ulong guildId,
        IReadOnlyCollection<ulong> roles,
        ImmutableArray<RoleColourOption> mappings)
    {
        return mappings
            .Where(o => HoldsSource(guildId: guildId, roles: roles, sourceId: o.SourceRoleId));
    }

    private static async Task RemoveColoursAsync(IGuildUser member, IEnumerable<ulong> colours, RequestOptions request)
    {
        foreach (var roleId in colours)
            await member.RemoveRoleAsync(roleId: roleId, options: request);
    }

    private static Optional<ulong> SoleColour(HashSet<ulong> colours) => colours.Count == 1 ? new Optional<ulong>(colours.Single()) : default;

    private static OperationFailureException RolesChangedError() =>
        OperationFailureException.Failure(description: "Discord roles changed during the update. Please check your roles and try again.");

    private static HashSet<ulong> ManagedDisplayRoles(IReadOnlyCollection<ulong> roles, IEnumerable<RoleColourOption> mappings)
    {
        HashSet<ulong> displayRoleIds = mappings
            .Select(mapping => mapping.DisplayRoleId)
            .ToHashSet();

        return roles
            .Where(displayRoleIds.Contains)
            .ToHashSet();
    }

    private async Task<Result<Optional<ulong>>> SetUserColourAsync(IGuild guild, ulong userId, ulong? colourId, CancellationToken ct)
    {
        var request = new RequestOptions { CancelToken = ct };
        Optional<IGuildUser> memberResult = await ReadMemberAsync(guild: guild, userId: userId, request: request);

        if (memberResult is not { HasValue: true, Value: var member })
            return new Result<Optional<ulong>>(OperationFailureException.NotFound(description: "The member is no longer in this guild."));

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guildId: guild.Id, ct: ct);

        if (colourId.HasValue)
        {
            ImmutableArray<IRole> eligible = EligibleColours(guild: guild, roles: member.RoleIds, mappings: mappings);

            if (!eligible
                    .Any(role => role.Id == colourId.Value))
                return new Result<Optional<ulong>>(
                    OperationFailureException.Validation(description: "That colour is no longer available to you."));
        }

        HashSet<ulong> worn = ManagedDisplayRoles(roles: member.RoleIds, mappings: mappings);

        HashSet<ulong> remove = worn
            .Where(id => id != colourId)
            .ToHashSet();
        var addColour = colourId.HasValue && !worn.Contains(colourId.Value);

        if (remove.Count == 0 && !addColour)
            return SoleColour(worn);

        await RemoveColoursAsync(member: member, colours: remove, request: request);

        if (addColour)
            await member.AddRoleAsync(roleId: colourId!.Value, options: request);

        Optional<IGuildUser> actualResult = await ReadMemberAsync(guild: guild, userId: userId, request: request);

        if (actualResult is not { HasValue: true, Value: var actual })
            return new Result<Optional<ulong>>(RolesChangedError());

        HashSet<ulong> actualColours = ManagedDisplayRoles(roles: actual.RoleIds, mappings: mappings);

        if (colourId.HasValue)
        {
            if (actualColours.Count != 1 || !actualColours.Contains(colourId.Value))
                return new Result<Optional<ulong>>(RolesChangedError());
        }
        else if (actualColours.Count != 0)
            return new Result<Optional<ulong>>(RolesChangedError());

        return SoleColour(actualColours);
    }

    private async Task<Result<Optional<ulong>>> ProcessRoleColourReconciliation(
        IGuild guild,
        ulong userId,
        Optional<ImmutableArray<ulong>> previousRoleIds,
        Optional<ImmutableArray<RoleColourOption>> previousMappings,
        CancellationToken ct)
    {
        var request = new RequestOptions { CancelToken = ct };
        Optional<IGuildUser> memberResult = await ReadMemberAsync(guild: guild, userId: userId, request: request);

        if (memberResult is not { HasValue: true, Value: var member })
            return new Result<Optional<ulong>>(OperationFailureException.NotFound(description: "The member is no longer in this guild."));

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guildId: guild.Id, ct: ct);
        ImmutableArray<RoleColourOption> old = previousMappings is { HasValue: true, Value: var supplied } ? supplied : mappings;

        HashSet<ulong> worn = ManagedDisplayRoles(
            roles: member.RoleIds,
            mappings
                .Concat(old));

        var holdsGrant = HeldGrants(guild.Id, member.RoleIds, mappings)
            .Any();

        HashSet<ulong> eligible = EligibleColours(guild, member.RoleIds, mappings)
            .Select(role => role.Id)
            .ToHashSet();

        IReadOnlyCollection<ulong> before = previousRoleIds is { HasValue: true, Value: var roles } ? roles : member.RoleIds;

        HashSet<ulong> previouslyGranted = HeldGrants(guild.Id, before, old)
            .Select(mapping => mapping.DisplayRoleId)
            .ToHashSet();
        IReadOnlySet<ulong> remove = RoleColourSelection.Removals(worn, holdsGrant, eligible, previouslyGranted);

        if (remove.Count == 0)
            return SoleColour(worn);

        await RemoveColoursAsync(member: member, colours: remove, request: request);
        Optional<IGuildUser> actualResult = await ReadMemberAsync(guild: guild, userId: userId, request: request);

        if (actualResult is not { HasValue: true, Value: var actual })
            return new Result<Optional<ulong>>(RolesChangedError());

        HashSet<ulong> actualColours = ManagedDisplayRoles(
            roles: actual.RoleIds,
            mappings
                .Concat(old));

        if (actualColours.Overlaps(remove))
            return new Result<Optional<ulong>>(RolesChangedError());

        return SoleColour(actualColours);
    }

    private async Task<ImmutableArray<RoleColourOption>> ReadMappingsAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        RoleColourOption[] mappings = await db.RoleColourOptions
            .AsNoTracking()
            .Where(o => o.GuildId == guildId)
            .ToArrayAsync(ct);

        return mappings
            .ToImmutableArray();
    }

    private async Task<Optional<IGuildUser>> ReadMemberAsync(IGuild guild, ulong userId, RequestOptions request)
    {
        var member =
            guild is SocketGuild
                ? await discordClient.Rest.GetGuildUserAsync(guildId: guild.Id, id: userId, options: request)
                : await guild.GetUserAsync(id: userId, mode: CacheMode.AllowDownload, options: request);

        return member is null ? default : new Optional<IGuildUser>(member);
    }
}