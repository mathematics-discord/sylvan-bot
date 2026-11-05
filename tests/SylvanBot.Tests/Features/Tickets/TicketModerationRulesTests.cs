namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketModerationRulesTests
{
    [TestCase(TicketAction.Timeout, true, false, true)]
    [TestCase(TicketAction.Timeout, false, true, false)]
    [TestCase(TicketAction.Ban, false, true, true)]
    [TestCase(TicketAction.Ban, true, false, false)]
    public void CanCorrect_OriginalModerator_RequiresMatchingPermission(TicketAction action, bool timeout, bool ban, bool allowed)
    {
        // Arrange
        var ticket = CreateTicket(action);
        var actor = new TicketActor(1, 2, false, timeout, ban);

        // Act
        var result = ticket.CanCorrect(actor);

        // Assert
        result.ShouldBe(allowed);
    }

    [TestCase(1ul, 3ul, false, false)]
    [TestCase(1ul, 3ul, true, true)]
    [TestCase(3ul, 2ul, true, false)]
    public void CanCorrect_Actor_EnforcesGuildAndSeniorAccess(ulong guild, ulong user, bool senior, bool expected)
    {
        // Arrange
        var ticket = CreateTicket(TicketAction.Timeout);
        var actor = new TicketActor(guild, user, false, true, true, CanViewAuditLog: senior);

        // Act
        var allowed = ticket.CanCorrect(actor);

        // Assert
        allowed.ShouldBe(expected);
        ticket.ModeratorUserId.ShouldBe(2UL);
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
        var observation = new TicketObservation(1, 1, TicketAction.Timeout, 2, 3, false, "", now)
        {
            Removed = newSeconds == 0,
            PreviousTimeout = previousSeconds == 0 ? new TicketTimeout.None() : new TicketTimeout.Until(now.AddSeconds(previousSeconds)),
            Timeout = newSeconds == 0 ? new TicketTimeout.None() : new TicketTimeout.Until(now.AddSeconds(newSeconds)),
        };

        // Act
        var result = observation.CreatesTicket;

        // Assert
        result.ShouldBe(creates);
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

    [TestCase(TicketAction.Kick)]
    [TestCase(TicketAction.VoiceMute)]
    [TestCase(TicketAction.VoiceDeafen)]
    [TestCase(TicketAction.Role)]
    public void CanCorrect_NewAction_RequiresOwnerAndMatchingPermission(TicketAction action)
    {
        // Arrange
        var ticket = CreateTicket(action);
        var owner = new TicketActor(1, 2, false, false,
            CanKickMembers: action == TicketAction.Kick,
            CanMuteMembers: action == TicketAction.VoiceMute,
            CanDeafenMembers: action == TicketAction.VoiceDeafen,
            CanManageRoles: action == TicketAction.Role);

        // Act
        var allowed = ticket.CanCorrect(owner);
        var other = ticket.CanCorrect(owner with { UserId = 9 });
        var unprivileged = ticket.CanCorrect(new TicketActor(1, 2, false, false));

        // Assert
        allowed.ShouldBeTrue();
        other.ShouldBeFalse();
        unprivileged.ShouldBeFalse();
    }

    private static Ticket CreateTicket(TicketAction action)
    {
        var ticket = new Ticket { GuildId = 1, Number = 1, SubjectUserId = 3, ModeratorUserId = 2,
            Action = action, CreatedAtUtc = DateTimeOffset.UtcNow };
        ticket.Initialise("reason", action == TicketAction.Timeout ? 3600 : null);
        return ticket;
    }
}
