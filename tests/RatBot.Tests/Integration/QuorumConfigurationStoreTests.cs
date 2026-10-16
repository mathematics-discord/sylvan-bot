using Dapper;
using Npgsql;
using RatBot.Features.Quorum.Persistence;
using RatBot.Features.Quorum;

namespace RatBot.Tests.Integration;

[TestFixture]
public sealed class QuorumConfigurationStoreTests
{
    private QuorumConfigurationStore _store = null!;

    private static QuorumScope.TextChannel TextScope()
    {
        return new QuorumScope.TextChannel(123, 456);
    }

    [Test]
    public async Task DeleteAsync_ShouldDeleteConfigurationAndRoles_AndReturnNotFoundWhenRepeated()
    {
        QuorumScope scope = TextScope();
        QuorumConfiguration configuration = (
            await _store.RegisterAsync(scope, QuorumProportion.Create(0.5m).Value, CancellationToken.None)
        ).Value.Configuration.AddRole(10);
        await _store.SaveAsync(configuration, CancellationToken.None);

        ErrorOr<Deleted> firstResult = await _store.DeleteAsync(scope, CancellationToken.None);
        ErrorOr<Deleted> secondResult = await _store.DeleteAsync(scope, CancellationToken.None);

        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);
        int roleCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM quorum_voter_roles");

        firstResult.IsError.ShouldBeFalse();
        secondResult.IsError.ShouldBeTrue();
        secondResult.FirstError.ShouldBe(QuorumErrors.ConfigurationNotFound);
        roleCount.ShouldBe(0);
    }

    [Test]
    public async Task GetAsync_ShouldRehydrateForumScope()
    {
        QuorumScope scope = new QuorumScope.ForumChannel(123, 456);
        QuorumProportion proportion = QuorumProportion.Create(0.5m).Value;
        await _store.RegisterAsync(scope, proportion, CancellationToken.None);

        ErrorOr<QuorumConfiguration> result = await _store.GetAsync(scope, CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Value.Scope.ShouldBeOfType<QuorumScope.ForumChannel>();
        result.Value.Scope.GuildId.ShouldBe(123UL);
        result.Value.Scope.ChannelId.ShouldBe(456UL);
    }

    [Test]
    public async Task GetAsync_ShouldReturnNotFound_WhenConfigurationIsMissing()
    {
        ErrorOr<QuorumConfiguration> result = await _store.GetAsync(TextScope(), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.ShouldBe(QuorumErrors.ConfigurationNotFound);
    }

    [Test]
    public async Task Migration_ShouldCreateOnlyTheRequiredQuorumColumns()
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);

        string[] configurationColumns = (
            await connection.QueryAsync<string>(
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'quorum_configurations'
                ORDER BY ordinal_position
                """
            )
        ).ToArray();

        string[] roleColumns = (
            await connection.QueryAsync<string>(
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'quorum_voter_roles'
                ORDER BY ordinal_position
                """
            )
        ).ToArray();

        configurationColumns.ShouldBe(["id", "guild_id", "channel_id", "channel_kind", "proportion"]);
        roleColumns.ShouldBe(["quorum_configuration_id", "role_id"]);
    }

    [Test]
    public async Task RegisterAsync_ShouldCreateThenUpdateConfiguration_WhilePreservingIdAndRoles()
    {
        QuorumScope scope = TextScope();
        QuorumProportion initialProportion = QuorumProportion.Create(0.5m).Value;
        QuorumProportion updatedProportion = QuorumProportion.Create(0.75m).Value;

        ErrorOr<QuorumRegistration> firstResult =
            await _store.RegisterAsync(scope, initialProportion, CancellationToken.None);
        firstResult.IsError.ShouldBeFalse();
        firstResult.Value.Created.ShouldBeTrue();

        QuorumConfiguration withRoles = firstResult.Value.Configuration.AddRole(20).AddRole(10);
        ErrorOr<QuorumConfiguration> saveResult = await _store.SaveAsync(withRoles, CancellationToken.None);
        saveResult.IsError.ShouldBeFalse();

        ErrorOr<QuorumRegistration> secondResult =
            await _store.RegisterAsync(scope, updatedProportion, CancellationToken.None);

        secondResult.IsError.ShouldBeFalse();
        secondResult.Value.Created.ShouldBeFalse();
        secondResult.Value.Configuration.Id.ShouldBe(firstResult.Value.Configuration.Id);
        secondResult.Value.Configuration.Proportion.ShouldBe(updatedProportion);
        secondResult.Value.Configuration.VoterRoles.RoleIds.Order().ShouldBe([10UL, 20UL]);
    }

    [Test]
    public async Task SaveAsync_ShouldReplaceProportionAndDeduplicatedRoles()
    {
        QuorumScope scope = TextScope();
        QuorumProportion initialProportion = QuorumProportion.Create(0.5m).Value;
        QuorumProportion updatedProportion = QuorumProportion.Create(0.8m).Value;
        QuorumConfiguration configuration =
            (await _store.RegisterAsync(scope, initialProportion, CancellationToken.None)).Value.Configuration;

        QuorumConfiguration updated =
            configuration.WithProportion(updatedProportion).AddRole(10).AddRole(10).AddRole(20);

        ErrorOr<QuorumConfiguration> saveResult = await _store.SaveAsync(updated, CancellationToken.None);
        ErrorOr<QuorumConfiguration> getResult = await _store.GetAsync(scope, CancellationToken.None);

        saveResult.IsError.ShouldBeFalse();
        getResult.IsError.ShouldBeFalse();
        getResult.Value.Proportion.ShouldBe(updatedProportion);
        getResult.Value.VoterRoles.RoleIds.Order().ShouldBe([10UL, 20UL]);
    }

    [Test]
    public async Task SaveAsync_ShouldReturnNotFound_WhenConfigurationIdIsMissing()
    {
        QuorumConfiguration configuration =
            QuorumConfiguration.Create(TextScope(), QuorumProportion.Create(0.5m).Value);

        ErrorOr<QuorumConfiguration> result = await _store.SaveAsync(configuration, CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.ShouldBe(QuorumErrors.ConfigurationNotFound);
    }

    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
        _store = PostgresDatabaseFixture.CreateQuorumConfigurationStore();
    }
}