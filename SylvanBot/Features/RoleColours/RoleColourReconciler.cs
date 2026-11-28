using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RoleColours;
using SylvanBot.Application.Infrastructure.Data;

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
        foreach (SemaphoreSlim gate in _guildGates.Values)
            gate.Dispose();
    }

    public async Task<ImmutableArray<IRole>> ListEligibleColoursAsync(IGuild guild, ulong userId, CancellationToken ct)
    {
        Optional<IGuildUser> memberResult = await ReadMemberAsync(guild: guild, userId: userId, new RequestOptions { CancelToken = ct });

        return memberResult is { HasValue: true, Value: var member }
            ? EligibleColours(guild: guild, roles: member.RoleIds, await ReadMappingsAsync(guildId: guild.Id, ct: ct))
            : [];
    }

    public async Task<Optional<ulong>> TrySetUserColourAsync(IGuild guild, ulong userId, ulong? colourId, CancellationToken ct)
    {
        SemaphoreSlim gate = _guildGates.GetOrAdd(key: guild.Id, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
        await gate.WaitAsync(ct);

        try
        {
            return await SetUserColourAsync(guild: guild, userId: userId, colourId: colourId, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not BotCommandException)
        {
            _logger.Error(
                exception: ex,
                "Role colour update incomplete for guild {GuildId}, member {MemberId}",
                propertyValue0: guild.Id,
                propertyValue1: userId);

            throw new BotCommandException("Discord could not complete the colour update. Your roles may have changed; please try again.", ex);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> ReconcileUserRolesAsync(
        IGuild guild,
        ulong userId,
        Optional<ImmutableArray<ulong>> previousRoleIds,
        Optional<ImmutableArray<RoleColourOption>> previousMappings,
        CancellationToken ct)
    {
        SemaphoreSlim gate = _guildGates.GetOrAdd(key: guild.Id, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
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

            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    // Old mappings are a temporary snapshot from the admin command, never persisted.
    public async Task<int> ReconcileGuildAsync(
        IGuild guild,
        Optional<ImmutableArray<RoleColourOption>> previousMappings,
        CancellationToken ct)
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

            foreach (IGuildUser member in members)
            {
                var participates = HeldGrants(guildId: guild.Id, roles: member.RoleIds, mappings: current).Any();
                var participated = HeldGrants(guildId: guild.Id, roles: member.RoleIds, mappings: old).Any();

                if (!participates && !participated)
                    continue;

                var complete = await ReconcileUserRolesAsync(
                    guild: guild,
                    userId: member.Id,
                    previousRoleIds: new Optional<ImmutableArray<ulong>>(member.RoleIds.ToImmutableArray()),
                    previousMappings: new Optional<ImmutableArray<RoleColourOption>>(old),
                    ct: ct);

                if (!complete)
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

    internal static string? MappingProblem(IGuild guild, ulong sourceId, ulong colourId)
    {
        IRole? source = guild.GetRole(sourceId);
        IRole? colour = guild.GetRole(colourId);

        if (source is null || source.Guild.Id != guild.Id)
            return "Source role does not exist in this guild.";

        if (colour is null || colour.Guild.Id != guild.Id)
            return "Colour role does not exist in this guild.";

        if (sourceId == colourId)
            return "Source and colour roles must differ.";

        if (source.Colors.PrimaryColor != Color.Default)
            return "Source role must not have a colour set.";

        if (colour.Colors.PrimaryColor == Color.Default)
            return "Colour role must have a colour set.";

        return null;
    }

    private static ImmutableArray<IRole> EligibleColours(IGuild guild, IReadOnlyCollection<ulong> roles, ImmutableArray<RoleColourOption> mappings) =>
    [
        .. HeldGrants(guildId: guild.Id, roles: roles, mappings: mappings)
            .Where(o => MappingProblem(guild: guild, sourceId: o.SourceRoleId, colourId: o.DisplayRoleId) is null)
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
        => mappings.Where(o => HoldsSource(guildId: guildId, roles: roles, sourceId: o.SourceRoleId));

    private static async Task RemoveColoursAsync(IGuildUser member, IEnumerable<ulong> colours, RequestOptions request)
    {
        foreach (var roleId in colours)
            await member.RemoveRoleAsync(roleId: roleId, options: request);
    }

    private static Optional<ulong> SoleColour(HashSet<ulong> colours) => colours.Count == 1
        ? new Optional<ulong>(colours.Single())
        : default;

    private static BotCommandException RolesChangedError()
        => new BotCommandException("Discord roles changed during the update. Please check your roles and try again.");

    private static HashSet<ulong> ManagedDisplayRoles(IReadOnlyCollection<ulong> roles, IEnumerable<RoleColourOption> mappings)
    {
        HashSet<ulong> displayRoleIds = mappings
            .Select(mapping => mapping.DisplayRoleId)
            .ToHashSet();

        return roles
            .Where(displayRoleIds.Contains)
            .ToHashSet();
    }

    private async Task<Optional<ulong>> SetUserColourAsync(IGuild guild, ulong userId, ulong? colourId, CancellationToken ct)
    {
        RequestOptions request = new RequestOptions { CancelToken = ct };
        Optional<IGuildUser> memberResult = await ReadMemberAsync(guild: guild, userId: userId, request: request);

        if (memberResult is not { HasValue: true, Value: var member })
            throw new BotCommandException("The member is no longer in this guild.");

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guildId: guild.Id, ct: ct);

        if (colourId.HasValue)
        {
            ImmutableArray<IRole> eligible = EligibleColours(guild: guild, roles: member.RoleIds, mappings: mappings);

            if (!eligible.Any(role => role.Id == colourId.Value))
                throw new BotCommandException("That colour is no longer available to you.");
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
            throw RolesChangedError();

        HashSet<ulong> actualColours = ManagedDisplayRoles(roles: actual.RoleIds, mappings: mappings);

        if (colourId.HasValue)
        {
            if (actualColours.Count != 1 || !actualColours.Contains(colourId.Value))
                throw RolesChangedError();
        }
        else if (actualColours.Count != 0)
        {
            throw RolesChangedError();
        }

        return SoleColour(actualColours);
    }

    private async Task<bool> ProcessRoleColourReconciliation(
        IGuild guild,
        ulong userId,
        Optional<ImmutableArray<ulong>> previousRoleIds,
        Optional<ImmutableArray<RoleColourOption>> previousMappings,
        CancellationToken ct)
    {
        RequestOptions request = new RequestOptions { CancelToken = ct };
        Optional<IGuildUser> memberResult = await ReadMemberAsync(guild: guild, userId: userId, request: request);

        if (memberResult is not { HasValue: true, Value: var member })
            return false;

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guildId: guild.Id, ct: ct);
        ImmutableArray<RoleColourOption> old = previousMappings is { HasValue: true, Value: var supplied } ? supplied : mappings;

        HashSet<ulong> worn = ManagedDisplayRoles(
            roles: member.RoleIds,
            mappings
                .Concat(old));

        var holdsGrant = HeldGrants(guildId: guild.Id, roles: member.RoleIds, mappings: mappings)
            .Any();

        HashSet<ulong> eligible = EligibleColours(guild: guild, roles: member.RoleIds, mappings: mappings)
            .Select(role => role.Id)
            .ToHashSet();

        IReadOnlyCollection<ulong> before = previousRoleIds is { HasValue: true, Value: var roles } ? roles : member.RoleIds;

        HashSet<ulong> previouslyGranted = HeldGrants(guildId: guild.Id, roles: before, mappings: old)
            .Select(mapping => mapping.DisplayRoleId)
            .ToHashSet();

        IReadOnlySet<ulong> remove = RoleColourSelection.Removals(
            worn: worn,
            holdsGrant: holdsGrant,
            eligible: eligible,
            previouslyGranted: previouslyGranted);

        if (remove.Count == 0)
            return true;

        await RemoveColoursAsync(member: member, colours: remove, request: request);
        Optional<IGuildUser> actualResult = await ReadMemberAsync(guild: guild, userId: userId, request: request);

        if (actualResult is not { HasValue: true, Value: var actual })
            return false;

        HashSet<ulong> actualColours = ManagedDisplayRoles(
            roles: actual.RoleIds,
            mappings
                .Concat(old));

        return !actualColours.Overlaps(remove);
    }

    private async Task<ImmutableArray<RoleColourOption>> ReadMappingsAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        RoleColourOption[] mappings = await db.RoleColourOptions
            .AsNoTracking()
            .Where(o => o.GuildId == guildId)
            .ToArrayAsync(ct);

        return mappings
            .ToImmutableArray();
    }

    private async Task<Optional<IGuildUser>> ReadMemberAsync(IGuild guild, ulong userId, RequestOptions request)
    {
        IGuildUser? member =
            guild is SocketGuild
                ? await discordClient.Rest.GetGuildUserAsync(guildId: guild.Id, id: userId, options: request)
                : await guild.GetUserAsync(id: userId, mode: CacheMode.AllowDownload, options: request);

        return member is null ? default : new Optional<IGuildUser>(member);
    }
}