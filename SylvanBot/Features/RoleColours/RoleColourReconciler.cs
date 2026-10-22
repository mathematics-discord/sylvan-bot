namespace SylvanBot.Features.RoleColours;

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Infrastructure.Data;

// Live Discord roles are the equipped state. Colour writes are serialized per guild.
public sealed class RoleColourReconciler(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger logger,
    DiscordSocketClient discordClient)
{
    internal const string RolesChangedMessage =
        "Discord roles changed during the update. Please check your roles and try again.";

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _guildGates = new();

    private readonly ILogger _logger = logger.ForContext<RoleColourReconciler>();

    public async Task<ImmutableArray<IRole>> ListEligibleColoursAsync(IGuild guild, ulong userId, CancellationToken ct)
    {
        Option<IGuildUser> memberResult = await ReadMemberAsync(guild, userId, new RequestOptions { CancelToken = ct });

        return memberResult.Value is Some<IGuildUser>(var member)
            ? EligibleColours(guild, member.RoleIds, await ReadMappingsAsync(guild.Id, ct))
            : [];
    }

    internal static string? MappingValidationMessage(IGuild guild, ulong sourceId, ulong colourId)
    {
        var source = guild.GetRole(sourceId);
        var colour = guild.GetRole(colourId);

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

    private static ImmutableArray<IRole> EligibleColours(IGuild guild,
        IReadOnlyCollection<ulong> roles,
        ImmutableArray<RoleColourOption> mappings) =>
    [
        .. HeldGrants(guild.Id, roles, mappings)
            .Where(o => MappingValidationMessage(guild, o.SourceRoleId, o.DisplayRoleId) is null)
            .Select(o => guild.GetRole(o.DisplayRoleId))
            .DistinctBy(role => role.Id)
            .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(role => role.Id),
    ];

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

    public async Task<Option<ulong>> TrySetUserColourAsync(IGuild guild,
        ulong userId,
        ulong? colourId,
        CancellationToken ct)
    {
        var gate = _guildGates.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            return await SetUserColourAsync(guild, userId, colourId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Option<ulong>> SetUserColourAsync(IGuild guild,
        ulong userId,
        ulong? colourId,
        CancellationToken ct)
    {
        var request = new RequestOptions { CancelToken = ct };
        Option<IGuildUser> memberResult = await ReadMemberAsync(guild, userId, request);

        if (memberResult.Value is not Some<IGuildUser>(var member))
            throw new KeyNotFoundException("The member is no longer in this guild.");

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guild.Id, ct);

        if (colourId.HasValue)
        {
            ImmutableArray<IRole> eligible = EligibleColours(guild, member.RoleIds, mappings);

            if (!eligible.Any(role => role.Id == colourId.Value))
                throw new ArgumentException("That colour is no longer available to you.", nameof(colourId));
        }

        HashSet<ulong> worn = ManagedDisplayRoles(member.RoleIds, mappings);
        HashSet<ulong> remove = worn.Where(id => id != colourId).ToHashSet();
        var addColour = colourId.HasValue && !worn.Contains(colourId.Value);

        if (remove.Count == 0 && !addColour)
            return SoleColour(worn);

        await RemoveColoursAsync(member, remove, request);

        if (addColour)
            await member.AddRoleAsync(colourId!.Value, request);

        Option<IGuildUser> actualResult = await ReadMemberAsync(guild, userId, request);

        if (actualResult.Value is not Some<IGuildUser>(var actual))
            throw new InvalidDataException(RolesChangedMessage);

        HashSet<ulong> actualColours = ManagedDisplayRoles(actual.RoleIds, mappings);

        if (colourId.HasValue)
        {
            if (actualColours.Count != 1 || !actualColours.Contains(colourId.Value))
                throw new InvalidDataException(RolesChangedMessage);
        }
        else if (actualColours.Count != 0)
        {
            throw new InvalidDataException(RolesChangedMessage);
        }

        return SoleColour(actualColours);
    }

    public async Task<Option<ulong>> ReconcileUserRolesAsync(
        IGuild guild,
        ulong userId,
        CancellationToken ct,
        IReadOnlyCollection<ulong>? previousRoleIds = null,
        ImmutableArray<RoleColourOption> previousMappings = default
    )
    {
        var gate = _guildGates.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            return await ProcessRoleColourReconciliation(guild, userId, previousRoleIds, previousMappings, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Option<ulong>> ProcessRoleColourReconciliation(
        IGuild guild,
        ulong userId,
        IReadOnlyCollection<ulong>? previousRoleIds,
        ImmutableArray<RoleColourOption> previousMappings,
        CancellationToken ct
    )
    {
        var request = new RequestOptions { CancelToken = ct };
        Option<IGuildUser> memberResult = await ReadMemberAsync(guild, userId, request);

        if (memberResult.Value is not Some<IGuildUser>(var member))
            throw new KeyNotFoundException("The member is no longer in this guild.");

        ImmutableArray<RoleColourOption> mappings = await ReadMappingsAsync(guild.Id, ct);
        ImmutableArray<RoleColourOption> old = previousMappings.IsDefault ? mappings : previousMappings;
        HashSet<ulong> worn = ManagedDisplayRoles(member.RoleIds, mappings.Concat(old));
        HashSet<ulong> remove;

        if (HeldGrants(guild.Id, member.RoleIds, mappings).Any())
        {
            Option<ulong> currentColour = SoleColour(worn);
            ImmutableArray<IRole> eligible = EligibleColours(guild, member.RoleIds, mappings);

            if (currentColour.Value is Some<ulong>(var colour) && eligible.Any(role => role.Id == colour))
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
        Option<IGuildUser> actualResult = await ReadMemberAsync(guild, userId, request);

        if (actualResult.Value is not Some<IGuildUser>(var actual))
            throw new InvalidDataException(RolesChangedMessage);

        HashSet<ulong> actualColours = ManagedDisplayRoles(actual.RoleIds, mappings.Concat(old));

        if (actualColours.Overlaps(remove))
            throw new InvalidDataException(RolesChangedMessage);

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

            var incomplete = 0;

            foreach (var member in members)
            {
                var participates = HeldGrants(guild.Id, member.RoleIds, current).Any();
                var participated = HeldGrants(guild.Id, member.RoleIds, old).Any();

                if (!participates && !participated)
                    continue;

                try
                {
                    await ReconcileUserRolesAsync(guild, member.Id, ct, member.RoleIds, old);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    incomplete++;
                    _logger.Error(ex, "Role colour reconciliation incomplete for guild {GuildId}, member {MemberId}",
                        guild.Id, member.Id);
                }
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
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return [.. await db.RoleColourOptions.AsNoTracking().Where(o => o.GuildId == guildId).ToArrayAsync(ct)];
    }

    private static async Task RemoveColoursAsync(IGuildUser member, IEnumerable<ulong> colours, RequestOptions request)
    {
        foreach (var roleId in colours)
            await member.RemoveRoleAsync(roleId, request);
    }

    private static Option<ulong> SoleColour(HashSet<ulong> colours)
    {
        return colours.Count == 1 ? new Some<ulong>(colours.Single()) : new None();
    }

    private async Task<Option<IGuildUser>> ReadMemberAsync(IGuild guild, ulong userId, RequestOptions request)
    {
        var member = guild is SocketGuild
            ? await discordClient.Rest.GetGuildUserAsync(guild.Id, userId, request)
            : await guild.GetUserAsync(userId, CacheMode.AllowDownload, request);

        return member is null ? new None() : new Some<IGuildUser>(member);
    }

    private static HashSet<ulong> ManagedDisplayRoles(IReadOnlyCollection<ulong> roles,
        IEnumerable<RoleColourOption> mappings)
    {
        HashSet<ulong> displayRoleIds = mappings.Select(mapping => mapping.DisplayRoleId).ToHashSet();
        return roles.Where(displayRoleIds.Contains).ToHashSet();
    }
}