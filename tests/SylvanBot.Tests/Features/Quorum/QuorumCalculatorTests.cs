using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Quorum;

namespace SylvanBot.Tests.Features.Quorum;

[TestFixture]
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
public sealed class QuorumCalculatorTests
{
    [Test]
    public void Calculate_OverlappingRolesAndDuplicateUsers_CountsEligibleNonBotsOnce()
    {
        // Arrange
        HashSet<ulong> voterRoleIds    = [10, 20];
        const decimal  proportionValue = 0.5m;

        QuorumMember[] members =
        [
            new(UserId: 1, IsBot: false, [10, 20]),
            new(UserId: 2, IsBot: false, [20]),
            new(UserId: 3, IsBot: true, [10]),
            new(UserId: 4, IsBot: false, [30]),
            new(UserId: 1, IsBot: false, [20]),
            new(UserId: 5, IsBot: false, []),
        ];

        // Act
        var result = QuorumCalculator.Calculate(members: members, voterRoleIds: voterRoleIds, proportion: proportionValue);

        // Assert
        result.ShouldBe(new QuorumCalculation(EligibleVoterCount: 2, Proportion: 0.5m, RequiredVotes: 1));
    }

    [Test]
    public void Calculate_NoVoterRoles_ReturnsIncompleteConfiguration()
    {
        // Arrange

        // ReSharper disable once CollectionNeverUpdated.Local - that's kinda the POINT you IDIOT
        HashSet<ulong> voterRoleIds    = [];
        const decimal  proportionValue = 0.5m;

        // Act
        var exception = Should.Throw<BotCommandException>(() => QuorumCalculator.Calculate(
                                                              [new QuorumMember(UserId: 1, IsBot: false, [10])],
                                                              voterRoleIds: voterRoleIds,
                                                              proportion: proportionValue));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.ConfigurationIncomplete);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_NoEligibleVoters_ReturnsMeaningfulError(bool emptyPopulation)
    {
        // Arrange
        HashSet<ulong> voterRoleIds    = [10];
        const decimal  proportionValue = 0.5m;

        QuorumMember[] members = emptyPopulation
            ? []
            : [new QuorumMember(UserId: 1, IsBot: true, [10]), new QuorumMember(UserId: 2, IsBot: false, [20])];

        // Act
        var exception =
            Should.Throw<BotCommandException>(() => QuorumCalculator.Calculate(members: members, voterRoleIds: voterRoleIds, proportion: proportionValue));

        // Assert
        exception.Message.ShouldBe(QuorumMessages.NoEligibleVoters);
    }

    [TestCase(arg1: 3, "0.5", arg3: 2)]
    [TestCase(arg1: 4, "0.5", arg3: 2)]
    [TestCase(arg1: 7, "0.6", arg3: 5)]
    [TestCase(arg1: 100, "0.00000001", arg3: 1)]
    [TestCase(arg1: 7, "1", arg3: 7)]
    public void Calculate_FractionalThreshold_RoundsRequiredVotesUpwards(int eligible, string proportion, int required)
    {
        // Arrange
        var            value        = decimal.Parse(s: proportion, provider: CultureInfo.InvariantCulture);
        HashSet<ulong> voterRoleIds = [10];

        QuorumMember[] members = Enumerable.Range(start: 1, count: eligible)
           .Select(id => new QuorumMember((ulong)id, IsBot: false, [10]))
           .ToArray();

        // Act
        var result = QuorumCalculator.Calculate(members: members, voterRoleIds: voterRoleIds, proportion: value);

        // Assert
        result.ShouldBe(new QuorumCalculation(EligibleVoterCount: eligible, Proportion: value, RequiredVotes: required));
    }
}