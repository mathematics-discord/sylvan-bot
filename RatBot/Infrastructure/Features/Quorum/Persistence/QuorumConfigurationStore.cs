using Dapper;
using Npgsql;
using RatBot.Application.Features.Quorum;
using RatBot.Domain.Features.Quorum;

namespace RatBot.Infrastructure.Features.Quorum.Persistence;

public sealed class QuorumConfigurationStore(string connectionString) : IQuorumConfigurationStore
{
    private const string ConfigurationColumns = """
                                                id AS "Id",
                                                guild_id AS "GuildId",
                                                channel_id AS "ChannelId",
                                                channel_kind AS "ChannelKind",
                                                proportion AS "Proportion"
                                                """;

    private static object ConfigurationParameters(Guid id, QuorumScope scope, QuorumProportion proportion)
    {
        return new
        {
            Id = id,
            GuildId = QuorumPersistenceMapping.ToDatabaseId(scope.GuildId),
            ChannelId = QuorumPersistenceMapping.ToDatabaseId(scope.ChannelId),
            ChannelKind = QuorumPersistenceMapping.ToChannelKind(scope),
            Proportion = proportion.Value
        };
    }

    private static QuorumPersistenceMapping.Data CreateData(Guid id, QuorumScope scope, QuorumProportion proportion)
    {
        return new QuorumPersistenceMapping.Data
        {
            Id = id,
            GuildId = QuorumPersistenceMapping.ToDatabaseId(scope.GuildId),
            ChannelId = QuorumPersistenceMapping.ToDatabaseId(scope.ChannelId),
            ChannelKind = QuorumPersistenceMapping.ToChannelKind(scope),
            Proportion = proportion.Value
        };
    }

    private static Task<IEnumerable<long>> GetRoleIdsAsync(
        NpgsqlConnection connection,
        Guid configurationId,
        NpgsqlTransaction? transaction,
        CancellationToken ct
    )
    {
        return connection.QueryAsync<long>(
            new CommandDefinition(
                """
                SELECT role_id
                FROM public.quorum_voter_roles
                WHERE quorum_configuration_id = @ConfigurationId
                ORDER BY role_id
                """,
                new { ConfigurationId = configurationId },
                transaction,
                cancellationToken: ct
            )
        );
    }

    private static async Task ReplaceRolesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        QuorumConfiguration configuration,
        CancellationToken ct
    )
    {
        await connection
            .ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM public.quorum_voter_roles WHERE quorum_configuration_id = @Id",
                    new { Id = configuration.Id.Value },
                    transaction,
                    cancellationToken: ct
                )
            )
            .ConfigureAwait(false);

        object[] roles = configuration
            .VoterRoles.RoleIds.Order()
            .Select(roleId => (object)new
            {
                QuorumConfigurationId = configuration.Id.Value, RoleId = QuorumPersistenceMapping.ToDatabaseId(roleId)
            })
            .ToArray();

        if (roles.Length == 0)
            return;

        await connection
            .ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO public.quorum_voter_roles (quorum_configuration_id, role_id)
                    VALUES (@QuorumConfigurationId, @RoleId)
                    """,
                    roles,
                    transaction,
                    cancellationToken: ct
                )
            )
            .ConfigureAwait(false);
    }

    private static object ScopeParameters(QuorumScope scope)
    {
        return new
        {
            GuildId = QuorumPersistenceMapping.ToDatabaseId(scope.GuildId),
            ChannelId = QuorumPersistenceMapping.ToDatabaseId(scope.ChannelId)
        };
    }

    public async Task<ErrorOr<Deleted>> DeleteAsync(QuorumScope scope, CancellationToken ct)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        int deleted = await connection
            .ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM public.quorum_configurations WHERE guild_id = @GuildId AND channel_id = @ChannelId",
                    ScopeParameters(scope),
                    cancellationToken: ct
                )
            )
            .ConfigureAwait(false);

        return deleted > 0 ? Result.Deleted : QuorumErrors.ConfigurationNotFound;
    }

    public async Task<ErrorOr<QuorumConfiguration>> GetAsync(QuorumScope scope, CancellationToken ct)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        QuorumPersistenceMapping.Data? data = await connection
            .QuerySingleOrDefaultAsync<QuorumPersistenceMapping.Data>(
                new CommandDefinition(
                    $"""
                     SELECT {ConfigurationColumns}
                     FROM public.quorum_configurations
                     WHERE guild_id = @GuildId AND channel_id = @ChannelId
                     """,
                    ScopeParameters(scope),
                    cancellationToken: ct
                )
            )
            .ConfigureAwait(false);

        return data is null
            ? QuorumErrors.ConfigurationNotFound
            : QuorumPersistenceMapping.ToDomain(data,
                await GetRoleIdsAsync(connection, data.Id, null, ct).ConfigureAwait(false));
    }

    public async Task<ErrorOr<QuorumRegistration>> RegisterAsync(QuorumScope scope, QuorumProportion proportion,
        CancellationToken ct)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        Guid id = QuorumConfigurationId.New().Value;
        object parameters = ConfigurationParameters(id, scope, proportion);

        int inserted = await connection
            .ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO public.quorum_configurations (id, guild_id, channel_id, channel_kind, proportion)
                    VALUES (@Id, @GuildId, @ChannelId, @ChannelKind, @Proportion)
                    ON CONFLICT (guild_id, channel_id) DO NOTHING
                    """,
                    parameters,
                    transaction,
                    cancellationToken: ct
                )
            )
            .ConfigureAwait(false);

        QuorumPersistenceMapping.Data data;
        if (inserted == 1)
            data = CreateData(id, scope, proportion);
        else
            data = await connection
                .QuerySingleAsync<QuorumPersistenceMapping.Data>(
                    new CommandDefinition(
                        $"""
                         UPDATE public.quorum_configurations
                         SET channel_kind = @ChannelKind, proportion = @Proportion
                         WHERE guild_id = @GuildId AND channel_id = @ChannelId
                         RETURNING {ConfigurationColumns}
                         """,
                        parameters,
                        transaction,
                        cancellationToken: ct
                    )
                )
                .ConfigureAwait(false);

        IEnumerable<long> roleIds = await GetRoleIdsAsync(connection, data.Id, transaction, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new QuorumRegistration(inserted == 1, QuorumPersistenceMapping.ToDomain(data, roleIds));
    }

    public async Task<ErrorOr<QuorumConfiguration>> SaveAsync(QuorumConfiguration configuration, CancellationToken ct)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        int updated = await connection
            .ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE public.quorum_configurations
                    SET proportion = @Proportion
                    WHERE id = @Id
                    """,
                    new { Id = configuration.Id.Value, Proportion = configuration.Proportion.Value },
                    transaction,
                    cancellationToken: ct
                )
            )
            .ConfigureAwait(false);

        if (updated == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return QuorumErrors.ConfigurationNotFound;
        }

        await ReplaceRolesAsync(connection, transaction, configuration, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return configuration;
    }

    private NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}