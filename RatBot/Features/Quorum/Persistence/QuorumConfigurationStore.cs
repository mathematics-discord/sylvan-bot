using Dapper;
using Npgsql;

namespace RatBot.Features.Quorum.Persistence;

public sealed class QuorumConfigurationStore(string connectionString)
{
    private const string ConfigurationColumns = """
                                               id AS "Id",
                                               guild_id AS "GuildId",
                                               channel_id AS "ChannelId",
                                               channel_kind AS "ChannelKind",
                                               proportion AS "Proportion"
                                               """;

    public async Task<ErrorOr<QuorumConfiguration>> GetAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        QuorumConfiguration? configuration = await connection.QuerySingleOrDefaultAsync<QuorumConfiguration>(
            new CommandDefinition(
                $"""
                 SELECT {ConfigurationColumns}
                 FROM public.quorum_configurations
                 WHERE guild_id = @GuildId AND channel_id = @ChannelId
                 """,
                new { GuildId = checked((long)guildId), ChannelId = checked((long)channelId) },
                cancellationToken: ct));

        if (configuration is null)
            return QuorumErrors.ConfigurationNotFound;

        IEnumerable<long> roleIds = await connection.QueryAsync<long>(
            new CommandDefinition(
                """
                SELECT role_id
                FROM public.quorum_voter_roles
                WHERE quorum_configuration_id = @Id
                """,
                new { configuration.Id },
                cancellationToken: ct));

        configuration.RoleIds = roleIds
            .Select(roleId => checked((ulong)roleId))
            .ToImmutableHashSet();

        return configuration;
    }

    public async Task<ErrorOr<bool>> RegisterAsync(
        IGuildChannel ch,
        decimal proportion,
        CancellationToken ct)
    {
        if (proportion is <= 0 or > 1)
            return Error.Validation(description: "Quorum proportion must be greater than 0 and at most 1.");

        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        Guid id = Guid.CreateVersion7();

        Guid configurationId = await connection.QuerySingleAsync<Guid>(
            new CommandDefinition(
                """
                 INSERT INTO public.quorum_configurations (id, guild_id, channel_id, channel_kind, proportion)
                 VALUES (@Id, @GuildId, @ChannelId, @ChannelKind, @Proportion)
                 ON CONFLICT (guild_id, channel_id) DO UPDATE
                 SET channel_kind = EXCLUDED.channel_kind, proportion = EXCLUDED.proportion
                 RETURNING id
                 """,
                new
                {
                    Id = id,
                    GuildId = checked((long)ch.GuildId),
                    ChannelId = checked((long)ch.Id),
                    ChannelKind = ch is IForumChannel ? "forum" : "text",
                    Proportion = proportion,
                },
                cancellationToken: ct));

        return configurationId == id;
    }

    public async Task<ErrorOr<Deleted>> DeleteAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        int deleted = await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM public.quorum_configurations WHERE guild_id = @GuildId AND channel_id = @ChannelId",
                new { GuildId = checked((long)guildId), ChannelId = checked((long)channelId) },
                cancellationToken: ct));

        return deleted > 0 ? Result.Deleted : QuorumErrors.ConfigurationNotFound;
    }

    public async Task<ErrorOr<Success>> UpdateRoleAsync(
        ulong guildId,
        ulong channelId,
        ulong roleId,
        bool shouldAdd,
        CancellationToken ct)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

        // Lock the configuration row to order conflicting role edits and coordinate with removal.
        // No saved snapshot of its proportion or other voter roles is written back.
        Guid? configurationId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                """
                SELECT id
                FROM public.quorum_configurations
                WHERE guild_id = @GuildId AND channel_id = @ChannelId
                FOR UPDATE
                """,
                new { GuildId = checked((long)guildId), ChannelId = checked((long)channelId) },
                transaction,
                cancellationToken: ct));

        if (configurationId is null)
            return QuorumErrors.ConfigurationNotFound;

        string sql = shouldAdd
            ? """
              INSERT INTO public.quorum_voter_roles (quorum_configuration_id, role_id)
              VALUES (@ConfigurationId, @RoleId)
              ON CONFLICT (quorum_configuration_id, role_id) DO NOTHING
              """
            : """
              DELETE FROM public.quorum_voter_roles
              WHERE quorum_configuration_id = @ConfigurationId AND role_id = @RoleId
              """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { ConfigurationId = configurationId.Value, RoleId = checked((long)roleId) },
                transaction,
                cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return Result.Success;
    }
}