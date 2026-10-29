namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public sealed class TicketModerationRulesTests
{
    [TestCase(TicketAction.Timeout, true, false, true)]
    [TestCase(TicketAction.Timeout, false, true, false)]
    [TestCase(TicketAction.Ban, false, true, true)]
    [TestCase(TicketAction.Ban, true, false, false)]
    public void CanCorrect_Owner_RequiresMatchingPermission(TicketAction action, bool timeout, bool ban, bool allowed)
    {
        // Arrange
        var ticket = new Ticket { GuildId = 1, ModeratorUserId = 2, Action = action };
        var actor = new TicketActor(1, 2, false, timeout, ban);

        // Act
        var result = ticket.CanCorrect(actor);

        // Assert
        result.ShouldBe(allowed);
    }

    [Test]
    public void CanCorrect_DifferentOwnerWithBanPermission_DoesNotOverrideTimeoutOwnership()
    {
        // Arrange
        var ticket = new Ticket { GuildId = 1, ModeratorUserId = 2, Action = TicketAction.Timeout };
        var actor = new TicketActor(1, 3, false, true, true);

        // Act
        var allowed = ticket.CanCorrect(actor);

        // Assert
        allowed.ShouldBeFalse();
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void CanCorrect_SeniorOrAdministrator_OverridesOwnership(bool administrator, bool audit)
    {
        // Arrange
        var ticket = new Ticket { GuildId = 1, ModeratorUserId = 2, Action = TicketAction.Ban, Hidden = true };
        var actor = new TicketActor(1, 3, administrator, false, CanViewAuditLog: audit);

        // Act
        var allowed = ticket.CanCorrect(actor);

        // Assert
        allowed.ShouldBeTrue();
    }

    [Test]
    public void CanCorrect_AnotherGuild_DeniesEvenAdministrator()
    {
        // Arrange
        var ticket = new Ticket { GuildId = 1, ModeratorUserId = 2, Action = TicketAction.Ban };
        var actor = new TicketActor(3, 2, true, true, true);

        // Act
        var allowed = ticket.CanCorrect(actor);

        // Assert
        allowed.ShouldBeFalse();
    }

    [Test]
    public void CanCorrect_HiddenTicketOwner_StillRequiresHiddenAccess()
    {
        // Arrange
        var ticket = new Ticket { GuildId = 1, ModeratorUserId = 2, Action = TicketAction.Timeout, Hidden = true };
        var actor = new TicketActor(1, 2, false, true);

        // Act
        var allowed = ticket.CanCorrect(actor);

        // Assert
        allowed.ShouldBeFalse();
    }

    [TestCase(false, TicketCompletion.Pending)]
    [TestCase(true, TicketCompletion.Complete)]
    public void UpdateCompletion_NullBanDuration_DistinguishesUnknownAndPermanent(bool known, TicketCompletion expected)
    {
        // Arrange
        var ticket = new Ticket { Action = TicketAction.Ban, Completion = TicketCompletion.Pending, Comment = "reason", DurationKnown = known };

        // Act
        ticket.UpdateCompletion();

        // Assert
        ticket.Completion.ShouldBe(expected);
    }

    [Test]
    public void UpdateCompletion_InactiveWithoutComment_RetainsCompletionObligation([Values] TicketStatus status)
    {
        // Arrange
        var ticket = new Ticket { Action = TicketAction.Timeout, Status = status, DurationKnown = true, DurationSeconds = 60, Completion = TicketCompletion.Pending };

        // Act
        ticket.UpdateCompletion();

        // Assert
        ticket.Completion.ShouldBe(TicketCompletion.Pending);
        ticket.Status.ShouldBe(status);
    }

    [TestCase(TicketAction.Timeout, TicketCompletion.Complete)]
    [TestCase(TicketAction.Ban, TicketCompletion.Complete)]
    public void UpdateCompletion_PendingDiscordOperation_SeparatesExpiryFromMissingDetails(TicketAction action, TicketCompletion expected)
    {
        // Arrange
        var ticket = new Ticket
        {
            Action = action,
            Completion = TicketCompletion.Pending,
            Comment = "reason",
            DurationKnown = true,
            DurationSeconds = 60,
            PendingOperationId = Guid.NewGuid(),
        };

        // Act
        ticket.UpdateCompletion();

        // Assert
        ticket.Completion.ShouldBe(expected);
    }

    [Test]
    public void UpdateCompletion_AutomatedUnknownDuration_DoesNotQueueBot()
    {
        // Arrange
        var ticket = new Ticket { Action = TicketAction.Ban, Completion = TicketCompletion.Automated };

        // Act
        ticket.UpdateCompletion();

        // Assert
        ticket.Completion.ShouldBe(TicketCompletion.Automated);
    }

    [TestCase(0, 60, true)]
    [TestCase(60, 120, true)]
    [TestCase(120, 60, false)]
    [TestCase(60, 0, false)]
    [TestCase(-60, 60, true)]
    public void CreatesTicket_TimeoutTransition_DistinguishesCreationAndCorrection(int previousSeconds, int newSeconds, bool creates)
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var observation = new TicketObservation(1, 1, TicketAction.Timeout, 2, 3, false, null, now,
            Removed: newSeconds == 0, PreviousExpiryUtc: previousSeconds == 0 ? null : now.AddSeconds(previousSeconds),
            ExpiryUtc: newSeconds == 0 ? null : now.AddSeconds(newSeconds));

        // Act
        var result = observation.CreatesTicket;

        // Assert
        result.ShouldBe(creates);
    }

    [Test]
    public void RejectOperation_TimeoutCorrection_PreservesObservedDurationAndCompletesDetails()
    {
        // Arrange
        var actionTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var originalExpiry = actionTime.AddHours(1);
        var ticket = new Ticket
        {
            Action = TicketAction.Timeout,
            Status = TicketStatus.Active,
            CreatedAtUtc = actionTime,
            DurationKnown = true,
            DurationSeconds = 3600,
            ExpiresAtUtc = originalExpiry,
            Comment = "reason",
            Completion = TicketCompletion.Pending,
            PublishedVersion = 2,
            Version = 2,
        };

        ticket.PrepareOperation(actionTime.AddHours(2), editorUserId: 3);
        ticket.PendingOperationAttempted = true;

        // Act
        ticket.RejectOperation();

        // Assert
        ticket.PendingOperationId.ShouldBeNull();
        ticket.PendingExpiryUtc.ShouldBeNull();
        ticket.PendingEditorUserId.ShouldBeNull();
        ticket.PendingRequestedAtUtc.ShouldBeNull();
        ticket.PendingOperationAttempted.ShouldBeFalse();
        ticket.ExpiresAtUtc.ShouldBe(originalExpiry);
        ticket.DurationSeconds.ShouldBe(3600);
        ticket.Status.ShouldBe(TicketStatus.Active);
        ticket.Completion.ShouldBe(TicketCompletion.Complete);
        ticket.Version.ShouldBeGreaterThan(ticket.PublishedVersion!.Value);
    }

    [Test]
    public void RejectOperation_BanExpiry_RetainsScheduleForRecovery()
    {
        // Arrange
        var expiry = DateTimeOffset.UtcNow.AddMinutes(-1);
        var ticket = new Ticket
        {
            Action = TicketAction.Ban,
            Status = TicketStatus.Active,
            ExpiresAtUtc = expiry,
            DurationKnown = true,
            DurationSeconds = 60,
            Comment = "reason",
            Completion = TicketCompletion.Complete,
        };

        ticket.PrepareOperation(expiry: null, editorUserId: null);
        ticket.PendingOperationAttempted = true;

        // Act
        ticket.RejectOperation();

        // Assert
        ticket.PendingOperationId.ShouldBeNull();
        ticket.Status.ShouldBe(TicketStatus.ExpirationFailed);
        ticket.ExpiresAtUtc.ShouldBe(expiry);
        ticket.ExpirySuspended.ShouldBeFalse();
        ticket.Completion.ShouldBe(TicketCompletion.Complete);
    }

    [TestCase(1ul, true)]
    [TestCase(2ul, false)]
    [TestCase(3ul, false)]
    public void CancelOlderOperation_AttemptedRequest_OnlyCancelsForLaterAudit(ulong lastAuditId, bool cancelled)
    {
        // Arrange
        var observationTime = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            PendingOperationId = Guid.NewGuid(),
            LastAuditId = lastAuditId,
            PendingRequestedAtUtc = observationTime.AddSeconds(1),
            PendingOperationAttempted = true,
        };

        var observation = new TicketObservation(1, 2, TicketAction.Ban, 3, 4, false, null, observationTime);

        // Act
        ticket.CancelOlderOperation(observation);

        // Assert
        (ticket.PendingOperationId is null).ShouldBe(cancelled);
        ticket.PendingOperationAttempted.ShouldBe(!cancelled);
    }

    [Test]
    public void ClearDelivery_CompletedQueue_CleansBindingOnceAndRetainsChannel()
    {
        // Arrange
        var moderator = new TicketModerator
        {
            UserId = 1,
            ActiveTicketId = 2,
            ChannelId = 3,
            PromptMessageId = 4,
            PromptVersion = 5,
            LastAttemptedVersion = 5,
            NextDeliveryUtc = DateTimeOffset.UtcNow,
            Version = 7,
        };

        // Act
        moderator.ClearDelivery();
        moderator.ClearDelivery();

        // Assert
        moderator.ActiveTicketId.ShouldBeNull();
        moderator.PromptMessageId.ShouldBeNull();
        moderator.PromptVersion.ShouldBeNull();
        moderator.LastAttemptedVersion.ShouldBeNull();
        moderator.NextDeliveryUtc.ShouldBeNull();
        moderator.ChannelId.ShouldBe((ulong)3);
        moderator.Version.ShouldBe(8);
    }

    [Test]
    public void IsDeliveryDue_VersionChangesBeforeDeadline_DoesNotSendAnotherReminder()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var moderator = new TicketModerator { ActiveTicketId = 1, NextDeliveryUtc = now.AddMinutes(5), LastAttemptedVersion = 1 };

        // Act
        moderator.PromptVersion = 2;
        moderator.Version++;
        var due = moderator.IsDeliveryDue(1, now.AddSeconds(30));

        // Assert
        due.ShouldBeFalse();
        moderator.IsDeliveryDue(1, now.AddMinutes(5)).ShouldBeTrue();
    }

    [Test]
    public void IsDeliveryDue_NewTicketOrExplicitRedelivery_SendsImmediately()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var moderator = new TicketModerator { ActiveTicketId = 1, NextDeliveryUtc = now.AddDays(1) };

        // Act
        var newTicketDue = moderator.IsDeliveryDue(2, now);
        moderator.NextDeliveryUtc = null;
        var requestedDue = moderator.IsDeliveryDue(1, now);

        // Assert
        newTicketDue.ShouldBeTrue();
        requestedDue.ShouldBeTrue();
    }

}
