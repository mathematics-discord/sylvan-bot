namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketTests
{
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

    [TestCase(TicketAction.Ban)]
    [TestCase(TicketAction.Timeout)]
    public void SetHidden_ActiveRestriction_PreservesActionAndPendingOperation(TicketAction action)
    {
        // Arrange
        var ticket = Create(action);
        var pending = new TicketOperation { Kind = action == TicketAction.Ban ? "RemoveBan" : "SetTimeoutExpiry", ExpiryUtc = DateTimeOffset.UtcNow };
        ticket.Pending = pending;
        DateTimeOffset? expiry = ticket.ExpiresAtUtc;
        var actor = new TicketActor(1, 3, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        ticket.SetHidden(actor, ticket.Version, hidden: true);

        // Assert
        ticket.Hidden.ShouldBeTrue();
        ticket.Status.ShouldBe(TicketStatus.Active);
        ticket.ExpiresAtUtc.ShouldBe(expiry);
        ticket.Pending.ShouldBeSameAs(pending);
        ticket.ModificationHistory.ShouldHaveSingleItem().Action.ShouldBe(TicketHistoryAction.Hidden);
    }

    [Test]
    public void SetHidden_RestoringEndedAction_ChangesOnlyVisibility()
    {
        // Arrange
        var ticket = Create(TicketAction.Ban);
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
    [TestCase("29 days", "Replacement")]
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
}