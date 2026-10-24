namespace SylvanBot.Tests.Integration;

using System.Diagnostics.CodeAnalysis;
using NSubstitute;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Quorum;

[TestFixture]
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class QuorumOperationsTests
{
    private IQuorumMembers _members = null!;
    private QuorumOperations _operations = null!;

    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
        _members = Substitute.For<IQuorumMembers>();
        _operations = new QuorumOperations(PostgresDatabaseFixture.CreateContextFactory(), _members);
    }

    [Test]
    public async Task CalculateAsync_MissingConfiguration_DoesNotRequestMembers()
    {
        // Arrange: no channel has been registered.

        // Act
        var exception = await Should.ThrowAsync<BotCommandException>(() =>
            _operations.CalculateAsync(100, 200, CancellationToken.None));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.ConfigurationNotFound);
        await _members.DidNotReceiveWithAnyArgs().GetMembersAsync(0, CancellationToken.None);
    }

    [Test]
    public async Task CalculateAsync_IncompleteConfiguration_DoesNotRequestMembers()
    {
        // Arrange
        await _operations.RegisterAsync(100, 200, "text", 0.5m, CancellationToken.None);

        // Act
        var exception =
            await Should.ThrowAsync<BotCommandException>(() =>
                _operations.CalculateAsync(100, 200, CancellationToken.None));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.ConfigurationIncomplete);
        await _members.DidNotReceiveWithAnyArgs().GetMembersAsync(default, default);
    }

    [Test]
    public async Task CalculateAsync_MemberDataFailure_PreservesError()
    {
        // Arrange
        await _operations.RegisterAsync(100, 200, "text", 0.5m, CancellationToken.None);
        await _operations.SetVoterRoleAsync(100, 200, 10, true, CancellationToken.None);

        _members.GetMembersAsync(100, CancellationToken.None)
            .Returns(Task.FromException<QuorumMember[]>(new BotCommandException(QuorumMessages.MemberDataUnavailable)));

        // Act
        var exception =
            await Should.ThrowAsync<BotCommandException>(() =>
                _operations.CalculateAsync(100, 200, CancellationToken.None));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.MemberDataUnavailable);
    }

    [Test]
    public async Task CalculateAsync_CompletePopulation_ReturnsCalculationWithStoredProportion()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await _operations.RegisterAsync(100, 200, "forum", 0.625m, cancellation.Token);
        await _operations.SetVoterRoleAsync(100, 200, 10, true, cancellation.Token);

        QuorumMember[] population = [new(1, false, [10]), new(2, false, [10])];

        _members.GetMembersAsync(100, cancellation.Token).Returns(population);

        // Act
        var result = await _operations.CalculateAsync(100, 200, cancellation.Token);

        // Assert
        result.ShouldBe(new QuorumCalculation(2, 0.625m, 2));
    }

    [TestCase("0")]
    [TestCase("-0.1")]
    [TestCase("1.01")]
    public async Task RegisterAsync_InvalidProportion_DoesNotWrite(string proportion)
    {
        // Arrange
        var value = decimal.Parse(proportion, System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var exception = await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _operations.RegisterAsync(
            100,
            200,
            "text",
            value,
            CancellationToken.None));

        // Assert
        exception.ParamName.ShouldBe("proportion");
    }

    [Test]
    public async Task CalculateAsync_MemberRetrievalCancelled_PropagatesCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await _operations.RegisterAsync(100, 200, "text", 0.5m, cancellation.Token);
        await _operations.SetVoterRoleAsync(100, 200, 10, true, cancellation.Token);

        _members.GetMembersAsync(100, cancellation.Token)
            .Returns(async _ =>
            {
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();

                return Array.Empty<QuorumMember>();
            });

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            _operations.CalculateAsync(100, 200, cancellation.Token));
    }

    [Test]
    public async Task RegisterAsync_CancelledCaller_DoesNotWrite()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            _operations.RegisterAsync(100, 200, "text", 0.5m, cancellation.Token));
    }
}