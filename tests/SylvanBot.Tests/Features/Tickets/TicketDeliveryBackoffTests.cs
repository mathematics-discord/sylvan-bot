using SylvanBot.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketDeliveryBackoffTests
{
    [Test]
    public void ModeratorFailed_LocalLimit_LeavesOtherDeliveryAvailable()
    {
        // Arrange
        var clock = new DeliveryClock();
        var backoff = new TicketDeliveryBackoff(clock);

        // Act
        backoff.ModeratorFailed(1);

        // Assert
        backoff.IsModeratorDeferred(1).ShouldBeTrue();
        backoff.IsModeratorDeferred(2).ShouldBeFalse();
        backoff.IsDeferred(1).ShouldBeFalse();
        backoff.IsDeferred(2).ShouldBeFalse();
    }

    [Test]
    public void Failed_ChannelLimit_LeavesOtherDeliveryAvailable()
    {
        // Arrange
        var backoff = new TicketDeliveryBackoff(new DeliveryClock());

        // Act
        backoff.Failed(1);

        // Assert
        backoff.IsDeferred(1).ShouldBeTrue();
        backoff.IsDeferred(2).ShouldBeFalse();
        backoff.IsModeratorDeferred(1).ShouldBeFalse();
    }

    [Test]
    public void DeferGlobal_ConfirmedGlobalLimit_DefersChannelsAndModerators()
    {
        // Arrange
        var clock = new DeliveryClock();
        var backoff = new TicketDeliveryBackoff(clock);

        // Act
        backoff.DeferGlobal(clock.GetUtcNow().AddMinutes(1));
        backoff.DeferGlobal(clock.GetUtcNow().AddSeconds(10));

        // Assert
        backoff.Remaining(1).ShouldBe(TimeSpan.FromMinutes(1));
        backoff.IsModeratorDeferred(2).ShouldBeTrue();
    }

    [Test]
    public void IsDeferred_DeadlineReached_AllowsDelivery()
    {
        // Arrange
        var clock = new DeliveryClock();
        var backoff = new TicketDeliveryBackoff(clock);
        backoff.Failed(1);
        backoff.ModeratorFailed(2);
        backoff.DeferGlobal(clock.GetUtcNow().AddSeconds(20));

        // Act
        clock.Advance(TimeSpan.FromSeconds(30));

        // Assert
        backoff.IsDeferred(1).ShouldBeFalse();
        backoff.IsModeratorDeferred(2).ShouldBeFalse();
        backoff.Remaining(1).ShouldBe(TimeSpan.Zero);
    }

    [TestCase(1, 30)]
    [TestCase(2, 60)]
    [TestCase(3, 120)]
    [TestCase(4, 240)]
    [TestCase(5, 300)]
    [TestCase(100, 300)]
    public void Failed_RepeatedFailures_CapsFallbackDelay(int failures, int seconds)
    {
        // Arrange
        var clock = new DeliveryClock();
        var backoff = new TicketDeliveryBackoff(clock);

        // Act
        for (var attempt = 0; attempt < failures; attempt++)
        {
            clock.Advance(backoff.Remaining(1));
            backoff.Failed(1);
        }

        // Assert
        backoff.Remaining(1).ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Test]
    public void Succeeded_DiscordDeadline_PreservesDeadlineAndResetsFailures()
    {
        // Arrange
        var clock = new DeliveryClock();
        var backoff = new TicketDeliveryBackoff(clock);
        backoff.Failed(1);
        backoff.Failed(1);
        backoff.DeferChannel(1, clock.GetUtcNow().AddMinutes(2));

        // Act
        backoff.Succeeded(1);

        // Assert
        backoff.Remaining(1).ShouldBe(TimeSpan.FromMinutes(2));
        clock.Advance(TimeSpan.FromMinutes(2));
        backoff.Failed(1);
        backoff.Remaining(1).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void ModeratorSucceeded_GlobalDeadline_PreservesGlobalCooldown()
    {
        // Arrange
        var clock = new DeliveryClock();
        var backoff = new TicketDeliveryBackoff(clock);
        backoff.ModeratorFailed(1);
        backoff.DeferGlobal(clock.GetUtcNow().AddMinutes(1));

        // Act
        backoff.ModeratorSucceeded(1);
        clock.Advance(TimeSpan.FromSeconds(30));

        // Assert
        backoff.IsModeratorDeferred(1).ShouldBeTrue();
        backoff.Remaining(2).ShouldBe(TimeSpan.FromSeconds(30));
    }

    private sealed class DeliveryClock : TimeProvider
    {
        private DateTimeOffset _now = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
