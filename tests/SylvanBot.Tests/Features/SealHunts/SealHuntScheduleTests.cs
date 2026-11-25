using SylvanBot.Features.SealHunts;

namespace SylvanBot.Tests.Features.SealHunts;

public sealed class SealHuntScheduleTests
{
    [Test]
    public void OnlyFirstClaimSetsDeadlineAndReplacementRejectsOldButtons()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        SealHuntWorker.ActiveSeal seal = new SealHuntWorker.ActiveSeal("first", 10);
        seal.Abandoned.Add(1);
        seal.Accepts("first", 10, now.AddHours(1)).ShouldBeTrue();

        DateTimeOffset deadline = seal.RecordClaim(2, now)!.Value;
        seal.RecordClaim(3, now.AddSeconds(1)).ShouldBeNull();
        seal.ExpiresAt.ShouldBe(deadline);
        seal.Accepts("first", 10, deadline.AddTicks(-1)).ShouldBeTrue();
        seal.Accepts("first", 10, deadline).ShouldBeFalse();

        SealHuntWorker.ActiveSeal replacement = new SealHuntWorker.ActiveSeal("second", 11);
        replacement.Accepts("first", 10, now).ShouldBeFalse();
        replacement.Accepts("second", 10, now).ShouldBeFalse();
        replacement.Accepts("second", 11, now).ShouldBeTrue();
        replacement.Abandoned.ShouldBeEmpty();
        replacement.Claimed.ShouldBeEmpty();
    }

    [Test]
    public void ClaimWindow_StaysWithinBoundsAndClustersAroundEightSeconds()
    {
        Random random = new Random(42);

        var seconds = Enumerable.Range(0, 10000)
            .Select(_ => SealHuntWorker.SampleClaimWindow(random).TotalSeconds)
            .Order()
            .ToArray();

        seconds[0].ShouldBeGreaterThanOrEqualTo(4);
        seconds[^1].ShouldBeLessThanOrEqualTo(16);
        seconds[5000].ShouldBeInRange(7.8, 8.2);
        // About 71.5% of a normal distribution truncated at two standard deviations lies within one.
        seconds.Count(value => value >= 8 / Math.Sqrt(2) && value <= 8 * Math.Sqrt(2)).ShouldBeInRange(6800, 7500);
    }

    [TestCase(2)]
    [TestCase(8)]
    [TestCase(14)]
    [TestCase(20)]
    public void OnSlot_NextWindowIsCentredSixHoursLater(int hour)
    {
        DateTimeOffset posted = new DateTimeOffset(2026, 11, 25, hour, 0, 0, TimeSpan.Zero);

        SealHuntWorker.NextHuntAt(posted, 0).ShouldBe(posted.AddHours(4));
        SealHuntWorker.NextHuntAt(posted, 0.5).ShouldBe(posted.AddHours(6));
        SealHuntWorker.NextHuntAt(posted, 1).ShouldBe(posted.AddHours(8));
    }

    [Test]
    public void OffSlotBoot_KeepsBothIntervalAndUtcWindow()
    {
        // 10:45 UTC. The next target is 14:00, but the earliest allowed arrival is 14:45.
        DateTimeOffset posted = new DateTimeOffset(2026, 11, 25, 19, 45, 0, TimeSpan.FromHours(9));

        SealHuntWorker.NextHuntAt(posted, 0).ShouldBe(posted.AddHours(4));
        SealHuntWorker.NextHuntAt(posted, 1).ShouldBe(new DateTimeOffset(2026, 11, 25, 16, 0, 0, TimeSpan.Zero));
    }
}