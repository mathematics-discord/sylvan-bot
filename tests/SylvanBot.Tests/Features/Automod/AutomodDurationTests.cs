using SylvanBot.Application.Features.Automod;

namespace SylvanBot.Tests.Features.Automod;

public sealed class AutomodDurationTests
{
    [TestCase(AutomodAction.Delete)]
    [TestCase(AutomodAction.Note)]
    public void ResolveSeconds_NonTimedAction_IgnoresEvenInvalidDuration(AutomodAction action)
    {
        // Act / Assert
        AutomodDuration.ResolveSeconds(action, new Optional<string>("not a duration")).HasValue.ShouldBeFalse();
        AutomodDuration.ResolveSeconds(action, default).HasValue.ShouldBeFalse();
    }

    [Test]
    public void ResolveSeconds_OmittedDuration_PreservesLegacyDefaults()
    {
        // Act / Assert
        AutomodDuration.ResolveSeconds(AutomodAction.Mute, default).Value.ShouldBe(2419200);
        AutomodDuration.ResolveSeconds(AutomodAction.Ban, default).HasValue.ShouldBeFalse();
    }

    [TestCase("30m", 1800L)]
    [TestCase("2 days", 172800L)]
    [TestCase(" 1 HOUR ", 3600L)]
    [TestCase("1s", 1L)]
    [TestCase("28d", 2419200L)]
    [TestCase("4 weeks", 2419200L)]
    public void ResolveSeconds_ValidMuteDuration_ReturnsWholeSeconds(string input, long expected)
    {
        // Act
        Optional<long> duration = AutomodDuration.ResolveSeconds(AutomodAction.Mute, new Optional<string>(input));

        // Assert
        duration.Value.ShouldBe(expected);
    }

    [TestCase(AutomodAction.Mute, "0s")]
    [TestCase(AutomodAction.Ban, "0 days")]
    [TestCase(AutomodAction.Mute, "-1s")]
    [TestCase(AutomodAction.Ban, "-2 days")]
    [TestCase(AutomodAction.Mute, "29d")]
    [TestCase(AutomodAction.Mute, "2419201s")]
    [TestCase(AutomodAction.Mute, "")]
    [TestCase(AutomodAction.Ban, "permanent")]
    [TestCase(AutomodAction.Ban, "1.5s")]
    [TestCase(AutomodAction.Ban, "10")]
    [TestCase(AutomodAction.Ban, "2 months")]
    public void ResolveSeconds_InvalidDuration_RejectsInput(AutomodAction action, string input)
    {
        // Act / Assert
        Should.Throw<FormatException>(() => AutomodDuration.ResolveSeconds(action, new Optional<string>(input)));
    }

    [TestCase("9223372036854775808s")]
    [TestCase("9223372036854775807d")]
    public void ResolveSeconds_Overflow_RejectsInput(string input)
    {
        // Act / Assert
        Should.Throw<OverflowException>(() => AutomodDuration.ResolveSeconds(AutomodAction.Ban, new Optional<string>(input)));
    }

    [Test]
    public void ResolveSeconds_BanLongerThanMuteLimit_IsAllowed()
    {
        // Act / Assert
        AutomodDuration.ResolveSeconds(AutomodAction.Ban, new Optional<string>("365d")).Value.ShouldBe(31536000);
    }

    [TestCase(1L)]
    [TestCase(2419200L)]
    public void MuteExpiry_ValidDuration_UsesRequestedSeconds(long seconds)
    {
        // Arrange
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        // Act
        var expiry = AutomodDuration.MuteExpiry(new Optional<long>(seconds), now);

        // Assert
        expiry.ShouldBe(now.AddSeconds(seconds));
    }

    [TestCase(-1L)]
    [TestCase(0L)]
    [TestCase(2419201L)]
    [TestCase(long.MaxValue)]
    public void MuteExpiry_InvalidDuration_RefusesWithoutClamping(long seconds)
    {
        // Act / Assert
        Should.Throw<ArgumentOutOfRangeException>(() => AutomodDuration.MuteExpiry(new Optional<long>(seconds), DateTimeOffset.UnixEpoch));
    }

    [Test]
    public void MuteExpiry_MissingStoredDuration_Refuses()
    {
        // Act / Assert
        Should.Throw<ArgumentOutOfRangeException>(() => AutomodDuration.MuteExpiry(default, DateTimeOffset.UnixEpoch));
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    public void CoversExpiry_ObservedTimeout_PreservesEqualOrLongerExpiry(int offsetSeconds, bool expected)
    {
        // Arrange
        var requested = DateTimeOffset.UnixEpoch.AddHours(1);
        Optional<DateTimeOffset> existing = new Optional<DateTimeOffset>(requested.AddSeconds(offsetSeconds));

        // Act / Assert
        AutomodDuration.CoversExpiry(existing, requested).ShouldBe(expected);
    }

    [Test]
    public void CoversExpiry_NoObservedTimeout_AllowsTimeout()
    {
        // Act / Assert
        AutomodDuration.CoversExpiry(default, DateTimeOffset.UnixEpoch.AddHours(1)).ShouldBeFalse();
    }
}