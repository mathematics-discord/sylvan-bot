namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketTests
{
    [Test]
    public void CompleteStaffReversal_RepeatedConfirmation_RetainsRequesterOnce()
    {
        // Arrange
        var ticket = Create(TicketAction.Ban);
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        ticket.CompleteStaffReversal(42, timestamp);
        ticket.CompleteStaffReversal(99, timestamp.AddMinutes(1));
        ticket.End(TicketStatus.Reverted, 99);

        // Assert
        ticket.Status.ShouldBe(TicketStatus.Reverted);
        ticket.ModificationHistory.Count.ShouldBe(1);
        ticket.ModificationHistory[0].Timestamp.ShouldBe(timestamp);
        ticket.ModificationHistory[0].Format().ShouldContain("<@42>");
    }

    [Test]
    public void Initialise_NoteAtCommentLimit_PreservesComment()
    {
        // Arrange
        var ticket = Create(TicketAction.Note);
        var comment = new string('a', Ticket.CommentLimit);

        // Act
        ticket.Initialise(comment, seconds: null);

        // Assert
        ticket.Comment.ShouldBe(comment);
        ticket.Complete.ShouldBeTrue();
        ticket.Status.ShouldBe(TicketStatus.NotApplicable);
    }

    [Test]
    [Combinatorial]
    public void SetHidden_ActiveRestriction_RejectsVisibilityChange(
        [Values(TicketAction.Ban, TicketAction.Timeout, TicketAction.VoiceMute, TicketAction.VoiceDeafen, TicketAction.Role)] TicketAction action,
        [Values] bool pendingOperation)
    {
        // Arrange
        var ticket = Create(action);
        var pending = pendingOperation
            ? new TicketOperation { Kind = action == TicketAction.Ban ? "RemoveBan" : "SetTimeoutExpiry", ExpiryUtc = DateTimeOffset.UtcNow }
            : null;

        ticket.Pending = pending;
        DateTimeOffset? expiry = ticket.ExpiresAtUtc;
        var version = ticket.Version;
        var actor = new TicketActor(1, 3, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        void Act() => ticket.SetHidden(actor, ticket.Version, hidden: true);

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.Hidden.ShouldBeFalse();
        ticket.Status.ShouldBe(TicketStatus.Active);
        ticket.Version.ShouldBe(version);
        ticket.ExpiresAtUtc.ShouldBe(expiry);
        ticket.Pending.ShouldBeSameAs(pending);
        ticket.ModificationHistory.ShouldBeEmpty();
    }

    [TestCase(TicketAction.Ban)]
    [TestCase(TicketAction.Timeout)]
    [TestCase(TicketAction.VoiceMute)]
    [TestCase(TicketAction.VoiceDeafen)]
    [TestCase(TicketAction.Role)]
    public void SetHidden_RevertedRestriction_HidesRecord(TicketAction action)
    {
        // Arrange
        var ticket = Create(action);
        var actor = new TicketActor(1, 3, IsAdministrator: true, CanTimeoutMembers: false);
        ticket.End(TicketStatus.Reverted, actor.UserId);

        // Act
        ticket.SetHidden(actor, ticket.Version, hidden: true);

        // Assert
        ticket.Hidden.ShouldBeTrue();
        ticket.Status.ShouldBe(TicketStatus.Reverted);
        ticket.ModificationHistory[^1].Action.ShouldBe(TicketHistoryAction.Hidden);
    }

    [TestCase(TicketAction.Ban)]
    [TestCase(TicketAction.Timeout)]
    [TestCase(TicketAction.VoiceMute)]
    [TestCase(TicketAction.VoiceDeafen)]
    [TestCase(TicketAction.Role)]
    public void SetHidden_RestoringEndedAction_ChangesOnlyVisibility(TicketAction action)
    {
        // Arrange
        var ticket = Create(action);
        var actor = new TicketActor(1, 3, IsAdministrator: true, CanTimeoutMembers: false);
        ticket.End(TicketStatus.Reverted);
        ticket.SetHidden(actor, ticket.Version, hidden: true);

        // Act
        ticket.SetHidden(actor, ticket.Version, hidden: false);

        // Assert
        ticket.Hidden.ShouldBeFalse();
        ticket.Status.ShouldBe(TicketStatus.Reverted);
        ticket.Pending.ShouldBeNull();
    }

    [TestCase("permanent", "Replacement")]
    [TestCase("10000 years", "Replacement")]
    public void ValidateEdit_InvalidCombinedTimeoutEdit_PreservesAllFields(string duration, string comment)
    {
        // Arrange
        var ticket = Create(TicketAction.Timeout);
        var details = TicketDetails.ParseFields(duration, comment);
        var version = ticket.Version;

        // Act
        void Act() => ticket.ValidateEdit(details, DateTimeOffset.UtcNow);

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.Comment.ShouldBe("reason");
        ticket.DurationSeconds.ShouldBe(3600);
        ticket.Version.ShouldBe(version);
        ticket.Pending.ShouldBeNull();
        ticket.ModificationHistory.ShouldBeEmpty();
    }

    [TestCase(1)]
    [TestCase(137)]
    [TestCase(93751)]
    public void ValidateEdit_ArbitraryTimeoutDuration_AcceptsWithoutClamping(long seconds)
    {
        // Arrange
        var ticket = Create(TicketAction.Timeout);
        var details = new TicketDetails(new TicketDurationEdit.Set(seconds), new TicketCommentEdit.Keep());

        // Act
        var comment = ticket.ValidateEdit(details, ticket.CreatedAtUtc);

        // Assert
        comment.ShouldBe("reason");
        details.TimeoutExpiry(ticket.CreatedAtUtc, ticket.CreatedAtUtc).ShouldBe(ticket.CreatedAtUtc.AddSeconds(seconds));
    }

    [Test]
    public void ValidateEdit_AppendExceedsLimit_PreservesComment()
    {
        // Arrange
        var ticket = Create(TicketAction.Note);
        ticket.Initialise(new string('a', Ticket.CommentLimit - 1), seconds: null);
        var details = new TicketDetails(new TicketDurationEdit.Keep(), new TicketCommentEdit.Append("b"));

        // Act
        void Act() => ticket.ValidateEdit(details, DateTimeOffset.UtcNow);

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.Comment.Length.ShouldBe(Ticket.CommentLimit - 1);
    }

    [Test]
    public void Complete_ReminderExemptWithoutComment_RemainsIncomplete()
    {
        // Arrange
        var ticket = new Ticket
        {
            GuildId = 1, Number = 1, SubjectUserId = 2, ModeratorUserId = 3,
            CreatedAtUtc = DateTimeOffset.UtcNow, Action = TicketAction.Ban, ReminderExempt = true
        };

        // Act
        ticket.Initialise(" ", seconds: null);

        // Assert
        ticket.ReminderExempt.ShouldBeTrue();
        ticket.Complete.ShouldBeFalse();
    }

    [TestCase(TicketStatus.Expired)]
    [TestCase(TicketStatus.Reverted)]
    public void End_MissingComment_RemainsIncomplete(TicketStatus status)
    {
        // Arrange
        var ticket = Create(TicketAction.Timeout);
        ticket.Initialise("", 3600);

        // Act
        ticket.End(status);

        // Assert
        ticket.Status.ShouldBe(status);
        ticket.Complete.ShouldBeFalse();
    }

    [Test]
    public void ValidateEdit_DefaultUnion_RejectsMissingChoice()
    {
        // Arrange
        var ticket = Create(TicketAction.Ban);
        var details = TicketDetails.ParseFields(duration: null, comment: null) with { Duration = default };

        // Act
        void Act() => ticket.ValidateEdit(details, DateTimeOffset.UtcNow);

        // Assert
        Should.Throw<BotCommandException>(Act);
        Should.Throw<BotCommandException>(() => new TicketDetails(default, new TicketCommentEdit.Keep()));
    }

    [TestCase(TicketStatus.Expired)]
    [TestCase(TicketStatus.Reverted)]
    [TestCase(TicketStatus.Superseded)]
    public void End_LifecycleChange_UpdatesStateWithoutModificationHistory(TicketStatus status)
    {
        // Arrange
        var ticket = Create(TicketAction.Timeout);
        var version = ticket.Version;

        // Act
        ticket.End(status, moderatorId: 4, successor: 2);

        // Assert
        ticket.Status.ShouldBe(status);
        ticket.SupersededByNumber.ShouldBe(2);
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.ShouldBeEmpty();
    }

    [Test]
    public void Record_AuditObserved_AdvancesVersionWithoutModificationHistory()
    {
        // Arrange
        var ticket = Create(TicketAction.Timeout);
        var version = ticket.Version;

        // Act
        ticket.Record(TicketHistoryAction.AuditObserved, moderatorId: 4);

        // Assert
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.ShouldBeEmpty();
    }

    [TestCase(60, "0.00:01:00")]
    [TestCase(90061, "1.01:01:01")]
    [TestCase(172800, "2.00:00:00")]
    public void SetDuration_FiniteDuration_FormatsHistoryAsDaysAndTime(long seconds, string expected)
    {
        // Arrange
        var ticket = Create(TicketAction.Ban);

        // Act
        ticket.SetDuration(seconds, 4);

        // Assert
        ticket.ModificationHistory[^1].Format().ShouldContain($"set duration to {expected} at ");
    }

    [Test]
    public void SetDuration_SystemChange_PreservesModeratorHistory()
    {
        // Arrange
        var ticket = Create(TicketAction.Timeout);
        ticket.SetDuration(600, 4);
        var version = ticket.Version;
        var history = ticket.ModificationHistory.ShouldHaveSingleItem();
        var originalDescription = history.Format();

        // Act
        ticket.SetDuration(300, moderatorId: null);

        // Assert
        ticket.DurationSeconds.ShouldBe(300);
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.ShouldHaveSingleItem().ShouldBeSameAs(history);
        history.Format().ShouldBe(originalDescription);
    }

    internal static Ticket Create(TicketAction action, bool reviewExempt = false)
    {
        var ticket = new Ticket
        {
            GuildId = 1, Number = 1, SubjectUserId = 2, ModeratorUserId = 3,
            CreatedAtUtc = DateTimeOffset.UtcNow, Action = action, ReviewExempt = reviewExempt
        };

        ticket.Initialise("reason", action == TicketAction.Timeout ? 3600 : null);

        return ticket;
    }

    [TestCase(0UL, 2UL, 3UL)]
    [TestCase(1UL, 0UL, 3UL)]
    [TestCase(1UL, 2UL, 0UL)]
    public void Initialise_MissingIdentity_RejectsCreation(ulong guild, ulong subject, ulong moderator)
    {
        // Arrange
        var ticket = new Ticket { GuildId = guild, Number = 1, SubjectUserId = subject, ModeratorUserId = moderator };

        // Act
        void Act() => ticket.Initialise("reason", seconds: null);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(Act);
    }
    [TestCase(TicketAction.VoiceMute)]
    [TestCase(TicketAction.VoiceDeafen)]
    [TestCase(TicketAction.Role)]
    public void ValidateEdit_ReversibleAction_AcceptsPermanentAndFiniteDuration(TicketAction action)
    {
        // Arrange
        var ticket = Create(action);

        // Act
        var finite = ticket.ValidateEdit(TicketDetails.ParseFields("1 hour", null), DateTimeOffset.UtcNow);
        var permanent = ticket.ValidateEdit(TicketDetails.ParseFields("permanent", null), DateTimeOffset.UtcNow);

        // Assert
        finite.ShouldBe("reason");
        permanent.ShouldBe("reason");
    }

    [Test]
    public void ValidateEdit_Kick_RejectsDuration()
    {
        // Arrange
        var ticket = Create(TicketAction.Kick);

        // Act
        void Act() => ticket.ValidateEdit(TicketDetails.ParseFields("1 hour", null), DateTimeOffset.UtcNow);

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.Status.ShouldBe(TicketStatus.Applied);
    }

    [TestCase(TicketAction.Ban, true)]
    [TestCase(TicketAction.Timeout, true)]
    [TestCase(TicketAction.Kick, false)]
    [TestCase(TicketAction.VoiceMute, false)]
    public void Initialise_CompleteAuditTicket_QueuesInitialPromptForBanAndTimeout(TicketAction action, bool expected)
    {
        // Arrange
        var ticket = new Ticket
        {
            GuildId = 1, Number = 1, SubjectUserId = 2, ModeratorUserId = 3,
            CreatedAtUtc = DateTimeOffset.UtcNow, Action = action, SourceAuditId = 10,
        };

        // Act
        ticket.Initialise("audit reason", action == TicketAction.Timeout ? 604800 : null);

        // Assert
        ticket.Complete.ShouldBeTrue();
        ticket.InitialPromptPending.ShouldBe(expected);
    }

    [TestCase(TicketAction.Ban)]
    [TestCase(TicketAction.Timeout)]
    public void Initialise_AutomatedAuditTicket_DoesNotPromptBot(TicketAction action)
    {
        // Arrange
        var ticket = new Ticket
        {
            GuildId = 1, Number = 1, SubjectUserId = 2, ModeratorUserId = 3,
            CreatedAtUtc = DateTimeOffset.UtcNow, Action = action, SourceAuditId = 10, ReminderExempt = true,
        };

        // Act
        ticket.Initialise("automated audit reason", action == TicketAction.Timeout ? 604800 : null);

        // Assert
        ticket.InitialPromptPending.ShouldBeFalse();
    }

}
