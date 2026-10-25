using System.Diagnostics.CodeAnalysis;

namespace SylvanBot.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Quorum;
using SylvanBot.Common;

[TestFixture]
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class QuorumPersistenceTests
{
    private QuorumOperations _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
        _store = new QuorumOperations(PostgresDatabaseFixture.CreateContextFactory(), Substitute.For<IQuorumMembers>());
    }

    [Test]
    public async Task RegisterAsync_UpdatesProportionWhilePreservingIdAndRoles()
    {
        var firstResult = await _store.RegisterAsync(123, 456, "text", 0.5m, CancellationToken.None);

        var id = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value.Id;

        await _store.SetVoterRoleAsync(123, 456, 20, true, CancellationToken.None);
        await _store.SetVoterRoleAsync(123, 456, 10, true, CancellationToken.None);

        var secondResult = await _store.RegisterAsync(123, 456, "text", 0.75m, CancellationToken.None);

        var stored = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value;

        firstResult.ShouldBeTrue();
        secondResult.ShouldBeFalse();
        stored.Id.ShouldBe(id);
        stored.Proportion.ShouldBe(0.75m);

        stored.VoterRoles
            .Select(role => role.RoleId)
            .Order()
            .ShouldBe([10UL, 20UL]);
    }

    [Test]
    public async Task SetVoterRoleAsync_ConcurrentIndependentEditsAndRegistration_PreserveEveryChange()
    {
        // Arrange: hold the parent row so all three role edits and registration overlap.
        await _store.RegisterAsync(123, 456, "text", 0.5m, CancellationToken.None);

        var id = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value.Id;
        await _store.SetVoterRoleAsync(123, 456, 30, true, CancellationToken.None);
        await _store.SetVoterRoleAsync(123, 456, 40, true, CancellationToken.None);
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT id FROM quorum_configurations WHERE id = {id} FOR UPDATE");

        // Act
        var addFirst = _store.SetVoterRoleAsync(123, 456, 10, true, CancellationToken.None);
        var addSecond = _store.SetVoterRoleAsync(123, 456, 20, true, CancellationToken.None);
        var remove = _store.SetVoterRoleAsync(123, 456, 30, false, CancellationToken.None);
        Task<bool> registration = _store.RegisterAsync(123, 456, "text", 0.8m, CancellationToken.None);
        await WaitForBlockedEditsAsync(4);
        await transaction.CommitAsync();
        await Task.WhenAll(addFirst, addSecond, remove, registration);

        // Assert
        var stored = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value;
        stored.Id.ShouldBe(id);
        stored.Proportion.ShouldBe(0.8m);

        stored.VoterRoles
            .Select(role => role.RoleId)
            .Order()
            .ShouldBe([10UL, 20UL, 40UL]);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SetVoterRoleAsync_ConflictingEditsToSameRole_FollowDatabaseOrder(bool addFirst)
    {
        await _store.RegisterAsync(123, 456, "text", 0.5m, CancellationToken.None);

        var id = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value.Id;
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT id FROM quorum_configurations WHERE id = {id} FOR UPDATE");

        var first = _store.SetVoterRoleAsync(123, 456, 10, addFirst, CancellationToken.None);
        await WaitForBlockedEditsAsync(1);
        var second = _store.SetVoterRoleAsync(123, 456, 10, !addFirst, CancellationToken.None);
        await WaitForBlockedEditsAsync(2);
        await transaction.CommitAsync();
        await Task.WhenAll(first, second);

        (await _store.InspectAsync(123, 456, CancellationToken.None)).Value.ShouldBeOfType<Some<QuorumConfiguration>>()
            .Value.VoterRoles
            .Any(role => role.RoleId == 10).ShouldBe(!addFirst);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SetVoterRoleAsync_WhenRemovalWins_ReturnsNotFound(bool shouldAdd)
    {
        await _store.RegisterAsync(123, 456, "text", 0.5m, CancellationToken.None);

        var id = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value.Id;
        await using var db = PostgresDatabaseFixture.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"DELETE FROM quorum_configurations WHERE id = {id}");

        var edit = _store.SetVoterRoleAsync(123, 456, 10, shouldAdd, CancellationToken.None);
        await WaitForBlockedEditsAsync(1);
        await transaction.CommitAsync();

        (await Should.ThrowAsync<BotCommandException>(() => edit)).Message.ShouldBe(
            QuorumMessages.ConfigurationNotFound);
    }

    [Test]
    public async Task SetVoterRoleAsync_EditsAreIdempotentAndScopedToGuildAndChannel()
    {
        await _store.RegisterAsync(123, 456, "text", 0.5m, CancellationToken.None);
        await _store.RegisterAsync(124, 456, "text", 0.75m, CancellationToken.None);

        await _store.SetVoterRoleAsync(123, 456, 10, true, CancellationToken.None);
        await _store.SetVoterRoleAsync(123, 456, 10, true, CancellationToken.None);

        (await _store.InspectAsync(123, 456, CancellationToken.None)).Value.ShouldBeOfType<Some<QuorumConfiguration>>()
            .Value.VoterRoles
            .Select(role => role.RoleId)
            .ShouldBe([10UL]);

        (await _store.InspectAsync(124, 456, CancellationToken.None)).Value.ShouldBeOfType<Some<QuorumConfiguration>>()
            .Value.VoterRoles
            .Select(role => role.RoleId)
            .ShouldBeEmpty();

        (await Should.ThrowAsync<BotCommandException>(() => _store.SetVoterRoleAsync(
                123,
                457,
                10,
                true,
                CancellationToken.None))).Message
            .ShouldBe(QuorumMessages.ConfigurationNotFound);

        await _store.SetVoterRoleAsync(123, 456, 10, false, CancellationToken.None);
        await _store.SetVoterRoleAsync(123, 456, 10, false, CancellationToken.None);

        (await _store.InspectAsync(123, 456, CancellationToken.None)).Value.ShouldBeOfType<Some<QuorumConfiguration>>()
            .Value.VoterRoles
            .Select(role => role.RoleId)
            .ShouldBeEmpty();
    }

    [Test]
    public async Task RegisterAsync_ConcurrentRegistrations_CreateOnceAndRetainIdentityAndRoles()
    {
        // Arrange
        Task<bool>[] registrations = Enumerable.Range(0, 8)
            .Select(_ => _store.RegisterAsync(123, 456, "text", 0.5m, CancellationToken.None))
            .ToArray();

        // Act
        var results = await Task.WhenAll(registrations);

        var original = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value;

        await _store.SetVoterRoleAsync(123, 456, 10, true, CancellationToken.None);

        IEnumerable<Task<bool>> updates = Enumerable.Range(0, 8)
            .Select(_ => _store.RegisterAsync(123, 456, "forum", 0.75m, CancellationToken.None));

        await Task.WhenAll(updates);

        var stored = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value;

        // Assert
        results.Count(result => result).ShouldBe(1);

        stored.Id.ShouldBe(original.Id);
        stored.ChannelKind.ShouldBe("forum");
        stored.Proportion.ShouldBe(0.75m);

        stored.VoterRoles.Select(role => role.RoleId).ShouldBe([10UL]);
    }

    [Test]
    public async Task RemoveAsync_OtherGuildOrChannel_PreservesConfigurationAndRoles()
    {
        // Arrange
        await _store.RegisterAsync(123, 456, "text", 0.5m, CancellationToken.None);
        await _store.SetVoterRoleAsync(123, 456, 10, true, CancellationToken.None);

        // Act
        var otherGuild = await _store.RemoveAsync(124, 456, CancellationToken.None);
        var otherChannel = await _store.RemoveAsync(123, 457, CancellationToken.None);
        Option<QuorumConfiguration> inspection = await _store.InspectAsync(124, 456, CancellationToken.None);

        var stored = (await _store.InspectAsync(123, 456, CancellationToken.None)).Value
            .ShouldBeOfType<Some<QuorumConfiguration>>().Value;

        // Assert
        otherGuild.ShouldBeFalse();
        otherChannel.ShouldBeFalse();
        inspection.Value.ShouldBeOfType<None>();

        stored.VoterRoles
            .Select(role => role.RoleId)
            .ShouldBe([10UL]);
    }

    private static async Task WaitForBlockedEditsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var db = PostgresDatabaseFixture.CreateDbContext();

        while (await db.Database
                   .SqlQuery<int>(
                       $"""
                        SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
                        WHERE datname = current_database()
                          AND wait_event_type = 'Lock'
                          AND query ILIKE '%quorum_configurations%'
                        """)
                   .SingleAsync(timeout.Token) < count)
        {
            await Task.Delay(25, timeout.Token);
        }
    }
}