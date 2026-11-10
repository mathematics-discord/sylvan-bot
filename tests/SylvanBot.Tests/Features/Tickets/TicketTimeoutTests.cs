using SylvanBot.Application.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketTimeoutTests
{
    private static readonly DateTimeOffset Started = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [TestCase(1)]
    [TestCase(28)]
    [TestCase(90)]
    [TestCase(365)]
    public void NativeTimeoutExpiry_FiniteDuration_BoundsOnlyNativeWindow(int days)
    {
        // Arrange
        var expiry = Started.AddDays(days);
        var expected = days < 28 ? expiry : Started.AddDays(28).AddMinutes(-1);

        // Act
        var applied = Ticket.NativeTimeoutExpiry(expiry, Started);

        // Assert
        applied.ShouldBe(expected);
    }

    [TestCase(arg1: 20, arg2: false)]
    [TestCase(arg1: 21, arg2: true)]
    [TestCase(arg1: 29, arg2: true)]
    [TestCase(arg1: 90, arg2: false)]
    public void NeedsTimeoutRenewal_LongTimeout_RenewsBeforeWindowExpires(int elapsedDays, bool expected)
    {
        // Arrange
        var ticket = Create();
        Extend(ticket, Started.AddDays(90), Started.AddDays(28));

        // Act
        var due = ticket.NeedsTimeoutRenewal(Started.AddDays(elapsedDays));

        // Assert
        due.ShouldBe(expected);
    }

    [Test]
    public void NativeTimeoutExpiry_SubmillisecondTime_ReturnsStableDiscordTimestamp()
    {
        // Arrange
        var now = Started.AddTicks(1234567);

        // Act
        var applied = Ticket.NativeTimeoutExpiry(now.AddDays(90), now);

        // Assert
        applied.ShouldBe(Started.AddDays(28).AddMinutes(-1).AddMilliseconds(123));
    }

    [Test]
    public void NeedsTimeoutRenewal_FinalWindow_DoesNotRenew()
    {
        // Arrange
        var ticket = Create();

        // Act
        var due = ticket.NeedsTimeoutRenewal(Started.AddDays(6));

        // Assert
        due.ShouldBeFalse();
    }

    [Test]
    public void NeedsTimeoutRenewal_SuspendedOrEnded_DoesNotRenew()
    {
        // Arrange
        var ticket = Create();
        Extend(ticket, Started.AddDays(90), Started.AddDays(28));
        ticket.ExpirySuspended = true;

        // Act
        var suspended = ticket.NeedsTimeoutRenewal(Started.AddDays(22));
        ticket.ExpirySuspended = false;
        ticket.End(TicketStatus.Reverted);
        var reverted = ticket.NeedsTimeoutRenewal(Started.AddDays(22));

        // Assert
        suspended.ShouldBeFalse();
        reverted.ShouldBeFalse();
    }

    [Test]
    public void ConfirmTimeoutChange_ModeratorExtension_PreservesDurationAndRequiresReview()
    {
        // Arrange
        var ticket = Create();
        var actor = new TicketActor(GuildId: 1, UserId: 3, IsAdministrator: true, CanTimeoutMembers: true);
        ticket.Approve(actor, ticket.Version);

        // Act
        Extend(ticket, Started.AddDays(90), Started.AddDays(28), requester: 3);

        // Assert
        ticket.ExpiresAtUtc.ShouldBe(Started.AddDays(90));
        ticket.DurationSeconds.ShouldBe(90L * 86400);
        ticket.AppliedTimeoutUntilUtc.ShouldBe(Started.AddDays(28));
        ticket.ApprovedByUserId.ShouldBeNull();
        ticket.Pending.ShouldBeNull();

        var lastChange = ticket.ModificationHistory
            .Last();

        lastChange.Action.ShouldBe(TicketHistoryAction.SetDuration);
    }

    [Test]
    public void ConfirmTimeoutChange_AutomaticRenewal_PreservesDurationAndModeratorHistory()
    {
        // Arrange
        var ticket = Create();
        Extend(ticket, Started.AddDays(90), Started.AddDays(28), requester: 3);
        var historyCount = ticket.ModificationHistory.Count;

        // Act
        Extend(ticket, Started.AddDays(90), Started.AddDays(49));

        // Assert
        ticket.ExpiresAtUtc.ShouldBe(Started.AddDays(90));
        ticket.DurationSeconds.ShouldBe(90L * 86400);
        ticket.AppliedTimeoutUntilUtc.ShouldBe(Started.AddDays(49));
        ticket.ModificationHistory.Count.ShouldBe(historyCount);
        ticket.Status.ShouldBe(TicketStatus.Active);
    }

    [TestCase(1)]
    [TestCase(7)]
    [TestCase(90)]
    public void ConfirmTimeoutChange_SuspendedRestriction_ResumesWithConfirmedDuration(int days)
    {
        // Arrange
        var ticket = new Ticket
        {
            GuildId = 1,
            Number = 1,
            SubjectUserId = 2,
            ModeratorUserId = 3,
            CreatedAtUtc = Started,
            Action = TicketAction.Timeout,
            ExpirySuspended = true,
        };

        ticket.Initialise("reason", 7L * 86400);
        ticket.ObserveTimeoutWindow(expiry: null, Started);
        var expiry = Started.AddDays(days);
        var nativeExpiry = Ticket.NativeTimeoutExpiry(expiry, Started);

        // Act
        Extend(ticket, expiry, nativeExpiry, requester: 3);

        // Assert
        ticket.ExpirySuspended.ShouldBeFalse();
        ticket.ExpiresAtUtc.ShouldBe(expiry);
        ticket.DurationSeconds.ShouldBe(days * 86400L);
        ticket.AppliedTimeoutUntilUtc.ShouldBe(nativeExpiry);
        ticket.Pending.ShouldBeNull();
        ticket.Status.ShouldBe(TicketStatus.Active);
    }

    [Test]
    public void ConfirmTimeoutChange_PendingOperationWithoutNativeWindow_UsesOriginalNativeExpiry()
    {
        // Arrange
        var ticket = Create();

        ticket.BeginTimeoutChange(
            new TicketOperation { Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = Started.AddDays(10), RequestedBy = 3 });

        // Act
        ticket.ConfirmTimeoutChange(Started);

        // Assert
        ticket.AppliedTimeoutUntilUtc.ShouldBe(Started.AddDays(10));
        ticket.ExpiresAtUtc.ShouldBe(Started.AddDays(10));
    }

    [Test]
    public void ConfirmTimeoutChange_ShortenedIntoPast_EndsRestriction()
    {
        // Arrange
        var ticket = Create();

        ticket.BeginTimeoutChange(
            new TicketOperation
            {
                Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = Started.AddHours(1), NativeExpiryUtc = Started.AddHours(1), RequestedBy = 3,
            });

        // Act
        ticket.ConfirmTimeoutChange(Started.AddDays(1));

        // Assert
        ticket.Status.ShouldBe(TicketStatus.Expired);
        ticket.ExpiresAtUtc.ShouldBe(Started.AddHours(1));
        ticket.Pending.ShouldBeNull();
    }

    private static Ticket Create()
    {
        var ticket = new Ticket
        {
            GuildId = 1,
            Number = 1,
            SubjectUserId = 2,
            ModeratorUserId = 3,
            CreatedAtUtc = Started,
            Action = TicketAction.Timeout,
        };

        ticket.Initialise("reason", 7L * 86400);
        return ticket;
    }

    private static void Extend(Ticket ticket, DateTimeOffset expiry, DateTimeOffset applied, ulong? requester = null)
    {
        ticket.BeginTimeoutChange(
            new TicketOperation
            {
                Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = expiry, NativeExpiryUtc = applied, RequestedBy = requester,
            });

        ticket.ConfirmTimeoutChange(Started);
    }
}