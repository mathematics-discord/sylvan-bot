using Discord;
using ErrorOr;
using NSubstitute;
using RatBot.Features.Quorum;
using Serilog;
using Shouldly;

namespace RatBot.Tests.Features.Quorum;

[TestFixture]
public sealed class QuorumCalculatorTests
{
    [Test]
    public async Task CalculateAsync_UsesCompletePopulation_ExcludesBotsAndCountsOverlappingRolesOnce()
    {
        // Arrange
        IGuild gld = Substitute.For<IGuild>();
        IGuildUser first = Member(1, false, 10, 20);
        IGuildUser second = Member(2, false, 20);
        IGuildUser bot = Member(3, true, 10);
        IGuildUser unrelated = Member(4, false, 30);
        gld.GetUsersAsync(CacheMode.CacheOnly, Arg.Any<RequestOptions>())
            .Returns(Task.FromResult<IReadOnlyCollection<IGuildUser>>([first]));
        gld.GetUsersAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>())
            .Returns(Task.FromResult<IReadOnlyCollection<IGuildUser>>([first, second, bot, unrelated, first]));
        QuorumConfiguration configuration = new QuorumConfiguration { Proportion = 0.5m, RoleIds = [10, 20] };

        // Act
        var result = await QuorumCalculator.CalculateAsync(
            gld, configuration, new LoggerConfiguration().CreateLogger(), CancellationToken.None);

        // Assert
        result.Value.ShouldBe((2, 1));
        await gld.DidNotReceive().GetUsersAsync(CacheMode.CacheOnly, Arg.Any<RequestOptions>());
    }

    [Test]
    public async Task CalculateAsync_FailedFetch_DoesNotFallBackToPartialCache()
    {
        IGuild gld = Substitute.For<IGuild>();
        IGuildUser cachedMember = Member(1, false, 10);
        gld.GetUsersAsync(CacheMode.CacheOnly, Arg.Any<RequestOptions>())
            .Returns(Task.FromResult<IReadOnlyCollection<IGuildUser>>([cachedMember]));
        Task<IReadOnlyCollection<IGuildUser>> failedFetch =
            Task.FromException<IReadOnlyCollection<IGuildUser>>(new HttpRequestException("Page fetch failed"));

        gld.GetUsersAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>()).Returns(failedFetch);
        QuorumConfiguration configuration = new QuorumConfiguration { RoleIds = [10] };

        var result = await QuorumCalculator.CalculateAsync(
            gld, configuration, new LoggerConfiguration().CreateLogger(), CancellationToken.None);

        result.FirstError.ShouldBe(QuorumErrors.MemberDataUnavailable);
        await gld.DidNotReceive().GetUsersAsync(CacheMode.CacheOnly, Arg.Any<RequestOptions>());
    }

    [Test]
    public async Task CalculateAsync_PropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        IGuild gld = Substitute.For<IGuild>();
        gld.GetUsersAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>())
            .Returns(Task.FromCanceled<IReadOnlyCollection<IGuildUser>>(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => QuorumCalculator.CalculateAsync(
            gld, new QuorumConfiguration { RoleIds = [10] },
            new LoggerConfiguration().CreateLogger(), cancellation.Token));
    }

    [TestCase(3, "0.5", 2)]
    [TestCase(4, "0.5", 2)]
    [TestCase(7, "0.6", 5)]
    [TestCase(100, "0.00000001", 1)]
    [TestCase(7, "1", 7)]
    public async Task CalculateAsync_RoundsRequiredVotesUpwards(int eligible, string proportion, int required)
    {
        decimal value = decimal.Parse(proportion, System.Globalization.CultureInfo.InvariantCulture);
        IGuild gld = Substitute.For<IGuild>();
        IGuildUser[] members = Enumerable.Range(1, eligible)
            .Select(id => Member((ulong)id, false, 10))
            .ToArray();

        gld.GetUsersAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>())
            .Returns(Task.FromResult<IReadOnlyCollection<IGuildUser>>(members));

        var result = await QuorumCalculator.CalculateAsync(
            gld, new QuorumConfiguration { Proportion = value, RoleIds = [10] },
            new LoggerConfiguration().CreateLogger(), CancellationToken.None);

        result.Value.ShouldBe((eligible, required));
    }

    private static IGuildUser Member(ulong id, bool isBot, params ulong[] roleIds)
    {
        IGuildUser member = Substitute.For<IGuildUser>();
        member.Id.Returns(id);
        member.IsBot.Returns(isBot);
        member.RoleIds.Returns(roleIds);
        return member;
    }
}