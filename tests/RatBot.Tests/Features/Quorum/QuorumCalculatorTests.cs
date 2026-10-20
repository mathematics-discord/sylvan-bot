namespace RatBot.Tests.Features.Quorum;

using System.Diagnostics.CodeAnalysis;
using RatBot.Application.Common;
using RatBot.Application.Features.Quorum;

[TestFixture]
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class QuorumCalculatorTests
{
    [Test]
    public void Calculate_OverlappingRolesAndDuplicateUsers_CountsEligibleNonBotsOnce()
    {
        // Arrange
        HashSet<ulong> voterRoleIds = [10, 20];
        const decimal proportionValue = 0.5m;

        QuorumMember[] members =
        [
            new(1, IsBot: false, [10, 20]),
            new(2, IsBot: false, [20]),
            new(3, IsBot: true, [10]),
            new(4, IsBot: false, [30]),
            new(1, IsBot: false, [20]),
            new(5, IsBot: false, []),
        ];

        // Act
        var result = QuorumCalculator.Calculate(members, voterRoleIds, proportionValue);

        // Assert
        result.ShouldBe(new QuorumCalculation(2, 0.5m, 1));
    }

    [Test]
    public void Calculate_NoVoterRoles_ReturnsIncompleteConfiguration()
    {
        // Arrange

        // ReSharper disable once CollectionNeverUpdated.Local - that's kinda the POINT you IDIOT
        HashSet<ulong> voterRoleIds = [];
        const decimal proportionValue = 0.5m;

        // Act
        var exception = Should.Throw<BotCommandException>(() => QuorumCalculator.Calculate(
            [new QuorumMember(1, IsBot: false, [10])],
            voterRoleIds,
            proportionValue));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.ConfigurationIncomplete);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_NoEligibleVoters_ReturnsMeaningfulError(bool emptyPopulation)
    {
        // Arrange
        HashSet<ulong> voterRoleIds = [10];
        const decimal proportionValue = 0.5m;

        QuorumMember[] members = emptyPopulation
            ? []
            : [new QuorumMember(1, IsBot: true, [10]), new QuorumMember(2, IsBot: false, [20])];

        // Act
        var exception =
            Should.Throw<BotCommandException>(() => QuorumCalculator.Calculate(members, voterRoleIds, proportionValue));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.NoEligibleVoters);
    }

    [TestCase(3, "0.5", 2)]
    [TestCase(4, "0.5", 2)]
    [TestCase(7, "0.6", 5)]
    [TestCase(100, "0.00000001", 1)]
    [TestCase(7, "1", 7)]
    public void Calculate_FractionalThreshold_RoundsRequiredVotesUpwards(int eligible, string proportion, int required)
    {
        // Arrange
        var value = decimal.Parse(proportion, System.Globalization.CultureInfo.InvariantCulture);
        HashSet<ulong> voterRoleIds = [10];

        QuorumMember[] members = Enumerable.Range(1, eligible)
            .Select(id => new QuorumMember((ulong)id, IsBot: false, [10]))
            .ToArray();

        // Act
        var result = QuorumCalculator.Calculate(members, voterRoleIds, value);

        // Assert
        result.ShouldBe(new QuorumCalculation(eligible, value, required));
    }
}