using SylvanBot.Application.Features.Automod;

namespace SylvanBot.Tests.Features.Automod;

public sealed class AutomodBanTests
{
    [TestCase("Pending", 0UL, false)]
    [TestCase("Active", 1UL, true)]
    [TestCase("Removing", 1UL, true)]
    [TestCase("Superseded", 1UL, false)]
    [TestCase("Suspended", 1UL, false)]
    public void CanExpire_DueOperation_RequiresConfirmedOwnership(string status, ulong auditId, bool expected)
    {
        // Arrange
        var now = DateTimeOffset.UnixEpoch.AddDays(1);

        var operation = new AutomodBan
        {
            Status = status, AuditId = auditId, LastAttemptAt = now.AddMinutes(-1),
            ExpiresAt = new Optional<DateTimeOffset>(now),
        };

        // Act / Assert
        operation.CanExpire(now).ShouldBe(expected);
        operation.LastAttemptAt = now;
        operation.CanExpire(now).ShouldBeFalse();
        operation.ExpiresAt = default;
        operation.CanExpire(now.AddMinutes(1)).ShouldBeFalse();
    }

    [TestCase(44, false)]
    [TestCase(45, true)]
    public void AuditCoverageLost_RetentionBoundary_SuspendsExpiry(int days, bool expected)
    {
        // Arrange
        var operation = new AutomodBan { CheckedAt = DateTimeOffset.UnixEpoch };

        // Act / Assert
        operation.AuditCoverageLost(DateTimeOffset.UnixEpoch.AddDays(days)).ShouldBe(expected);
    }

    [Test]
    public void BanExpiry_NoDuration_IsPermanent()
    {
        // Act / Assert
        AutomodDuration.BanExpiry(default, DateTimeOffset.UnixEpoch).HasValue.ShouldBeFalse();
    }

    [Test]
    public void BanExpiry_PositiveDuration_UsesRequestedTime()
    {
        // Act / Assert
        AutomodDuration.BanExpiry(new Optional<long>(60), DateTimeOffset.UnixEpoch)
            .Value
            .ShouldBe(DateTimeOffset.UnixEpoch.AddMinutes(1));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    public void BanExpiry_InvalidDuration_Refuses(long seconds)
    {
        // Act / Assert
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AutomodDuration.BanExpiry(new Optional<long>(seconds), DateTimeOffset.UnixEpoch));
    }

    [Test]
    public void Observe_OriginalBanThenNewBan_PreservesReplacement()
    {
        // Arrange
        var operation = new AutomodBan();

        // Act
        operation.Observe(100, ownBan: true, ownRemoval: false);
        operation.Observe(101, ownBan: false, ownRemoval: false);

        // Assert
        operation.AuditId.ShouldBe(100UL);
        operation.Status.ShouldBe("Superseded");
    }

    [Test]
    public void Observe_RepeatedConfirmation_DoesNotChangeOwnership()
    {
        // Arrange
        var operation = new AutomodBan();
        operation.Observe(100, ownBan: true, ownRemoval: false);

        // Act
        operation.Observe(100, ownBan: false, ownRemoval: false);

        // Assert
        operation.Status.ShouldBe("Active");
    }

    [Test]
    public void Observe_UncertainUnbanConfirmed_EndsOperation()
    {
        // Arrange
        var operation = new AutomodBan { AuditId = 100, Status = "Removing" };

        // Act
        operation.Observe(101, ownBan: false, ownRemoval: true);

        // Assert
        operation.Status.ShouldBe("Ended");
    }
}