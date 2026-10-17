using Dapper;
using Discord;
using Npgsql;
using NSubstitute;
using RatBot.Features.Quorum.Persistence;
using RatBot.Features.Quorum;

namespace RatBot.Tests.Integration;

[TestFixture]
public sealed class QuorumConfigurationStoreTests
{
    private QuorumConfigurationStore _store = null!;
    private ITextChannel _ch = null!;

    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
        _store = PostgresDatabaseFixture.CreateQuorumConfigurationStore();
        _ch = Substitute.For<ITextChannel>();
        _ch.GuildId.Returns(123UL);
        _ch.Id.Returns(456UL);
    }

    [Test]
    public async Task DeleteAsync_DeletesConfigurationAndRoles_AndReturnsNotFoundWhenRepeated()
    {
        await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);
        await _store.UpdateRoleAsync(_ch.GuildId, _ch.Id, 10, true, CancellationToken.None);

        ErrorOr<Deleted> firstResult = await _store.DeleteAsync(_ch.GuildId, _ch.Id, CancellationToken.None);
        ErrorOr<Deleted> secondResult = await _store.DeleteAsync(_ch.GuildId, _ch.Id, CancellationToken.None);

        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);
        int roleCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM quorum_voter_roles");

        firstResult.IsError.ShouldBeFalse();
        secondResult.FirstError.ShouldBe(QuorumErrors.ConfigurationNotFound);
        roleCount.ShouldBe(0);
    }

    [Test]
    public async Task GetAsync_PreservesForumKindAndGuildChannelIdentity()
    {
        IForumChannel forum = Substitute.For<IForumChannel>();
        forum.GuildId.Returns(123UL);
        forum.Id.Returns(456UL);
        await _store.RegisterAsync(forum, 0.5m, CancellationToken.None);

        QuorumConfiguration result = (await _store.GetAsync(123, 456, CancellationToken.None)).Value;

        result.ChannelKind.ShouldBe("forum");
        result.GuildId.ShouldBe(123UL);
        result.ChannelId.ShouldBe(456UL);
    }

    [Test]
    public async Task GetAsync_ReturnsNotFoundWhenMissing()
    {
        ErrorOr<QuorumConfiguration> result = await _store.GetAsync(123, 456, CancellationToken.None);

        result.FirstError.ShouldBe(QuorumErrors.ConfigurationNotFound);
    }

    [Test]
    public async Task Migration_ShouldCreateOnlyTheRequiredQuorumColumns()
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);

        string[] configurationColumns = (await connection.QueryAsync<string>(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'quorum_configurations'
            ORDER BY ordinal_position
            """)).ToArray();

        string[] roleColumns = (await connection.QueryAsync<string>(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'quorum_voter_roles'
            ORDER BY ordinal_position
            """)).ToArray();

        configurationColumns.ShouldBe(["id", "guild_id", "channel_id", "channel_kind", "proportion"]);
        roleColumns.ShouldBe(["quorum_configuration_id", "role_id"]);
    }

    [Test]
    public async Task RegisterAsync_UpdatesProportionWhilePreservingIdAndRoles()
    {
        var firstResult = await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);
        Guid id = (await _store.GetAsync(123, 456, CancellationToken.None)).Value.Id;
        await _store.UpdateRoleAsync(123, 456, 20, true, CancellationToken.None);
        await _store.UpdateRoleAsync(123, 456, 10, true, CancellationToken.None);

        var secondResult = await _store.RegisterAsync(_ch, 0.75m, CancellationToken.None);
        QuorumConfiguration stored = (await _store.GetAsync(123, 456, CancellationToken.None)).Value;

        firstResult.Value.ShouldBeTrue();
        secondResult.Value.ShouldBeFalse();
        stored.Id.ShouldBe(id);
        stored.Proportion.ShouldBe(0.75m);
        stored.RoleIds.Order().ShouldBe([10UL, 20UL]);
    }

    [Test]
    public async Task UpdateRoleAsync_ConcurrentIndependentEditsAndRegistration_PreserveEveryChange()
    {
        // Arrange: hold the parent row so all three role edits and registration overlap.
        await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);
        Guid id = (await _store.GetAsync(123, 456, CancellationToken.None)).Value.Id;
        await _store.UpdateRoleAsync(123, 456, 30, true, CancellationToken.None);
        await _store.UpdateRoleAsync(123, 456, 40, true, CancellationToken.None);
        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SELECT id FROM quorum_configurations WHERE id = @Id FOR UPDATE",
            new { Id = id }, transaction);

        // Act
        Task<ErrorOr<Success>> addFirst = _store.UpdateRoleAsync(123, 456, 10, true, CancellationToken.None);
        Task<ErrorOr<Success>> addSecond = _store.UpdateRoleAsync(123, 456, 20, true, CancellationToken.None);
        Task<ErrorOr<Success>> remove = _store.UpdateRoleAsync(123, 456, 30, false, CancellationToken.None);
        var registration = _store.RegisterAsync(_ch, 0.8m, CancellationToken.None);
        await WaitForBlockedEditsAsync(4);
        await transaction.CommitAsync();
        await Task.WhenAll(addFirst, addSecond, remove, registration);

        // Assert
        addFirst.Result.IsError.ShouldBeFalse();
        addSecond.Result.IsError.ShouldBeFalse();
        remove.Result.IsError.ShouldBeFalse();
        registration.Result.IsError.ShouldBeFalse();
        QuorumConfiguration stored = (await _store.GetAsync(123, 456, CancellationToken.None)).Value;
        stored.Id.ShouldBe(id);
        stored.Proportion.ShouldBe(0.8m);
        stored.RoleIds.Order().ShouldBe([10UL, 20UL, 40UL]);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task UpdateRoleAsync_ConflictingEditsToSameRole_FollowDatabaseOrder(bool addFirst)
    {
        await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);
        Guid id = (await _store.GetAsync(123, 456, CancellationToken.None)).Value.Id;
        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SELECT id FROM quorum_configurations WHERE id = @Id FOR UPDATE",
            new { Id = id }, transaction);

        Task<ErrorOr<Success>> first = _store.UpdateRoleAsync(123, 456, 10, addFirst, CancellationToken.None);
        await WaitForBlockedEditsAsync(1);
        Task<ErrorOr<Success>> second = _store.UpdateRoleAsync(123, 456, 10, !addFirst, CancellationToken.None);
        await WaitForBlockedEditsAsync(2);
        await transaction.CommitAsync();
        await Task.WhenAll(first, second);

        first.Result.IsError.ShouldBeFalse();
        second.Result.IsError.ShouldBeFalse();
        (await _store.GetAsync(123, 456, CancellationToken.None)).Value.RoleIds.Contains(10).ShouldBe(!addFirst);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task UpdateRoleAsync_WhenRemovalWins_ReturnsNotFound(bool shouldAdd)
    {
        await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);
        Guid id = (await _store.GetAsync(123, 456, CancellationToken.None)).Value.Id;
        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("DELETE FROM quorum_configurations WHERE id = @Id", new { Id = id }, transaction);

        Task<ErrorOr<Success>> edit = _store.UpdateRoleAsync(123, 456, 10, shouldAdd, CancellationToken.None);
        await WaitForBlockedEditsAsync(1);
        await transaction.CommitAsync();

        (await edit).FirstError.ShouldBe(QuorumErrors.ConfigurationNotFound);
    }

    [Test]
    public async Task UpdateRoleAsync_EditsAreIdempotentAndScopedToGuildAndChannel()
    {
        await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);
        ITextChannel otherGuildChannel = Substitute.For<ITextChannel>();
        otherGuildChannel.GuildId.Returns(124UL);
        otherGuildChannel.Id.Returns(456UL);
        await _store.RegisterAsync(otherGuildChannel, 0.75m, CancellationToken.None);

        await _store.UpdateRoleAsync(123, 456, 10, true, CancellationToken.None);
        await _store.UpdateRoleAsync(123, 456, 10, true, CancellationToken.None);
        (await _store.GetAsync(123, 456, CancellationToken.None)).Value.RoleIds.ShouldBe([10UL]);
        (await _store.GetAsync(124, 456, CancellationToken.None)).Value.RoleIds.ShouldBeEmpty();
        (await _store.UpdateRoleAsync(123, 457, 10, true, CancellationToken.None)).FirstError
            .ShouldBe(QuorumErrors.ConfigurationNotFound);

        await _store.UpdateRoleAsync(123, 456, 10, false, CancellationToken.None);
        await _store.UpdateRoleAsync(123, 456, 10, false, CancellationToken.None);
        (await _store.GetAsync(123, 456, CancellationToken.None)).Value.RoleIds.ShouldBeEmpty();
    }

    private static async Task WaitForBlockedEditsAsync(int count)
    {
        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using NpgsqlConnection connection = new NpgsqlConnection(PostgresDatabaseFixture.ConnectionString);
        await connection.OpenAsync(timeout.Token);

        while (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                   """
                   SELECT COUNT(*) FROM pg_stat_activity
                   WHERE datname = current_database()
                     AND wait_event_type = 'Lock'
                     AND query ILIKE '%quorum_configurations%'
                   """, cancellationToken: timeout.Token)) < count)
        {
            await Task.Delay(25, timeout.Token);
        }
    }
}