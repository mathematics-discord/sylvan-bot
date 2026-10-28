using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Application.Features.Quorum;

public sealed class QuorumOperations(IDbContextFactory<BotDbContext> contexts, IQuorumMembers members)
{
    public async Task<QuorumCalculation> CalculateAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        Option<QuorumConfiguration> configuration = await InspectAsync(guildId: guildId, channelId: channelId, ct: ct);

        if (configuration.Value is not Some<QuorumConfiguration>(var configured))
            throw new BotCommandException(QuorumMessages.ConfigurationNotFound);

        HashSet<ulong> voterRoleIds = configured.VoterRoles
           .Select(role => role.RoleId)
           .ToHashSet();

        if (voterRoleIds.Count == 0)
            throw new BotCommandException(QuorumMessages.ConfigurationIncomplete);

        QuorumMember[] population = await members.GetMembersAsync(guildId: guildId, ct: ct);
        ct.ThrowIfCancellationRequested();

        return QuorumCalculator.Calculate(members: population, voterRoleIds: voterRoleIds, proportion: configured.Proportion);
    }

    public async Task<Option<QuorumConfiguration>> InspectAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var configuration = await db.QuorumConfigurations
           .AsNoTracking()
           .Include(configuration => configuration.VoterRoles)
           .SingleOrDefaultAsync(
                configuration => configuration.GuildId == guildId && configuration.ChannelId == channelId,
                cancellationToken: ct);

        return configuration is null ? new None() : new Some<QuorumConfiguration>(configuration);
    }

    public async Task<bool> RegisterAsync(
        ulong guildId,
        ulong channelId,
        string channelKind,
        decimal proportion,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (proportion is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(proportion), actualValue: proportion, message: QuorumMessages.InvalidProportion);

        if (channelKind is not ("text" or "forum"))
            throw new ArgumentOutOfRangeException(nameof(channelKind), actualValue: channelKind, "Unsupported quorum channel kind.");

        await using var db = await contexts.CreateDbContextAsync(ct);
        var id = Guid.CreateVersion7();
        var storedGuildId = checked((long)guildId);
        var storedChannelId = checked((long)channelId);

        // The upsert locks an existing parent row and retains its identity and voter roles.
        List<Guid> configurationIds = await db.Database
           .SqlQuery<Guid>(
                $"""
                 INSERT INTO public.quorum_configurations (id, guild_id, channel_id, channel_kind, proportion)
                 VALUES ({id}, {storedGuildId}, {storedChannelId}, {channelKind}, {proportion})
                 ON CONFLICT (guild_id, channel_id) DO UPDATE
                 SET channel_kind = EXCLUDED.channel_kind, proportion = EXCLUDED.proportion
                 RETURNING id AS "Value"
                 """)
           .ToListAsync(ct);

        return configurationIds[0] == id;
    }

    public async Task<bool> RemoveAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var deleted = await db.QuorumConfigurations
           .Where(configuration => configuration.GuildId == guildId && configuration.ChannelId == channelId)
           .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    public async Task SetVoterRoleAsync(
        ulong guildId,
        ulong channelId,
        ulong roleId,
        bool shouldAdd,
        CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var storedGuildId = checked((long)guildId);
        var storedChannelId = checked((long)channelId);
        var storedRoleId = checked((long)roleId);

        // Keep this lock and the targeted write on the same context and transaction.
        List<Guid> configurationIds = await db.Database
           .SqlQuery<Guid>(
                $"""
                 SELECT id AS "Value" FROM public.quorum_configurations
                 WHERE guild_id = {storedGuildId} AND channel_id = {storedChannelId}
                 FOR UPDATE
                 """)
           .ToListAsync(ct);

        if (configurationIds.Count == 0)
            throw new BotCommandException(QuorumMessages.ConfigurationNotFound);

        var configurationId = configurationIds[0];

        if (shouldAdd)
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO public.quorum_voter_roles (quorum_configuration_id, role_id)
                 VALUES ({configurationId}, {storedRoleId})
                 ON CONFLICT (quorum_configuration_id, role_id) DO NOTHING
                 """,
                cancellationToken: ct);
        }
        else
        {
            await db.QuorumVoterRoles
               .Where(role => role.QuorumConfigurationId == configurationId && role.RoleId == roleId)
               .ExecuteDeleteAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}