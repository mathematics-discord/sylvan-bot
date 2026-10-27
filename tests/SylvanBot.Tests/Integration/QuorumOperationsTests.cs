using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using NSubstitute;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Quorum;

namespace SylvanBot.Tests.Integration;

[TestFixture]
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class QuorumOperationsTests
{
    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
        _members    = Substitute.For<IQuorumMembers>();
        _operations = new QuorumOperations(PostgresDatabaseFixture.CreateContextFactory(), members: _members);
    }

    private IQuorumMembers   _members    = null!;
    private QuorumOperations _operations = null!;

    [Test]
    public async Task CalculateAsync_MissingConfiguration_DoesNotRequestMembers()
    {
        // Arrange: no channel has been registered.

        // Act
        var exception = await Should.ThrowAsync<BotCommandException>(() =>
                                                                         _operations.CalculateAsync(guildId: 100, channelId: 200, ct: CancellationToken.None));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.ConfigurationNotFound);
        await _members.DidNotReceiveWithAnyArgs().GetMembersAsync(guildId: 0, ct: CancellationToken.None);
    }

    [Test]
    public async Task CalculateAsync_IncompleteConfiguration_DoesNotRequestMembers()
    {
        // Arrange
        await _operations.RegisterAsync(guildId: 100, channelId: 200, "text", proportion: 0.5m, ct: CancellationToken.None);

        // Act
        var exception =
            await Should.ThrowAsync<BotCommandException>(() =>
                                                             _operations.CalculateAsync(guildId: 100, channelId: 200, ct: CancellationToken.None));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.ConfigurationIncomplete);
        await _members.DidNotReceiveWithAnyArgs().GetMembersAsync(guildId: default, ct: default);
    }

    [Test]
    public async Task CalculateAsync_MemberDataFailure_PreservesError()
    {
        // Arrange
        await _operations.RegisterAsync(guildId: 100, channelId: 200, "text", proportion: 0.5m, ct: CancellationToken.None);
        await _operations.SetVoterRoleAsync(guildId: 100, channelId: 200, roleId: 10, shouldAdd: true, ct: CancellationToken.None);

        _members.GetMembersAsync(guildId: 100, ct: CancellationToken.None)
           .Returns(Task.FromException<QuorumMember[]>(new BotCommandException(QuorumMessages.MemberDataUnavailable)));

        // Act
        var exception =
            await Should.ThrowAsync<BotCommandException>(() =>
                                                             _operations.CalculateAsync(guildId: 100, channelId: 200, ct: CancellationToken.None));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.MemberDataUnavailable);
    }

    [Test]
    public async Task CalculateAsync_CompletePopulation_ReturnsCalculationWithStoredProportion()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await _operations.RegisterAsync(guildId: 100, channelId: 200, "forum", proportion: 0.625m, ct: cancellation.Token);
        await _operations.SetVoterRoleAsync(guildId: 100, channelId: 200, roleId: 10, shouldAdd: true, ct: cancellation.Token);

        QuorumMember[] population = [new(UserId: 1, IsBot: false, [10]), new(UserId: 2, IsBot: false, [10])];

        _members.GetMembersAsync(guildId: 100, ct: cancellation.Token).Returns(population);

        // Act
        var result = await _operations.CalculateAsync(guildId: 100, channelId: 200, ct: cancellation.Token);

        // Assert
        result.ShouldBe(new QuorumCalculation(EligibleVoterCount: 2, Proportion: 0.625m, RequiredVotes: 2));
    }

    [TestCase("0")]
    [TestCase("-0.1")]
    [TestCase("1.01")]
    public async Task RegisterAsync_InvalidProportion_DoesNotWrite(string proportion)
    {
        // Arrange
        var value = decimal.Parse(s: proportion, provider: CultureInfo.InvariantCulture);

        // Act
        var exception = await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _operations.RegisterAsync(
                                                                                 guildId: 100,
                                                                                 channelId: 200,
                                                                                 "text",
                                                                                 proportion: value,
                                                                                 ct: CancellationToken.None));

        // Assert
        exception.ParamName.ShouldBe("proportion");
    }

    [Test]
    public async Task CalculateAsync_MemberRetrievalCancelled_PropagatesCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await _operations.RegisterAsync(guildId: 100, channelId: 200, "text", proportion: 0.5m, ct: cancellation.Token);
        await _operations.SetVoterRoleAsync(guildId: 100, channelId: 200, roleId: 10, shouldAdd: true, ct: cancellation.Token);

        _members.GetMembersAsync(guildId: 100, ct: cancellation.Token)
           .Returns(async _ =>
            {
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();

                return Array.Empty<QuorumMember>();
            });

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
                                                                _operations.CalculateAsync(guildId: 100, channelId: 200, ct: cancellation.Token));
    }

    [Test]
    public async Task RegisterAsync_CancelledCaller_DoesNotWrite()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
                                                                _operations.RegisterAsync(guildId: 100, channelId: 200, "text", proportion: 0.5m, ct: cancellation.Token));
    }
}