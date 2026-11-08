namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.Tickets;

public sealed class TicketApprovalTests
{
    [Test]
    public void AwaitingApprovalIncludesBothCutoverChannelsAndOmitsExemptTicketsAndNotes()
    {
        var live = new Ticket { GuildId = 1, Action = TicketAction.Timeout, PinnedPublicationChannelId = 10 };
        var archive = new Ticket { GuildId = 1, Action = TicketAction.Ban, ImportedFromLegacy = true, PinnedPublicationChannelId = 20 };
        var pending = new[]
        {
            archive, live,
            new Ticket { GuildId = 1, Action = TicketAction.Note },
            new Ticket { GuildId = 1, Action = TicketAction.Ban, ReviewExempt = true },
            new Ticket { GuildId = 2, Action = TicketAction.Ban },
        }.AsQueryable();

        TicketCoordinator.AwaitingApproval(pending, 1).ToArray().ShouldBe(new[] { archive, live });
    }

    [Test]
    public void Approve_SeniorReviewer_RecordsOnlyOneApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        var reviewer = new TicketActor(1, 4, false, false, CanViewAuditLog: true);

        // Act
        ticket.Approve(reviewer, ticket.Version);
        ticket.Approve(reviewer, ticket.Version);

        // Assert
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ApprovedAtUtc.ShouldNotBeNull();
        ticket.Version.ShouldBe(1);
        ticket.ModificationHistory.ShouldHaveSingleItem().Format().ShouldStartWith("- <@4> approved at <t:");
    }

    [Test]
    public void Approve_OrdinaryModerator_RejectsApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        var moderator = new TicketActor(1, 3, false, true, true);

        // Act
        void Act() => ticket.Approve(moderator, ticket.Version);

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.ApprovedByUserId.ShouldBeNull();
    }

    [Test]
    public void Approve_StaleVersion_RejectsApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);

        // Act
        void Act() => ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version + 1);

        // Assert
        Should.Throw<BotCommandException>(Act);
    }

    [Test]
    public void Approve_IncompleteOrPending_RejectsApproval([Values] bool pending)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        if (pending)
            ticket.Pending = new TicketOperation { Kind = "RemoveBan" };
        else ticket.Initialise("", null);

        // Act
        void Act() => ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);

        // Assert
        Should.Throw<BotCommandException>(Act);
    }

    [Test]
    public void SetComment_ApprovedTicket_PreservesApproval([Values] bool append)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;
        var version = ticket.Version;
        var comment = append ? "reason\nAdditional detail" : "Changed";
        var action = append ? TicketHistoryAction.AppendedComment : TicketHistoryAction.SetComment;

        // Act
        ticket.SetComment(comment, 3, append: append);

        // Assert
        ticket.Comment.ShouldBe(comment);
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ApprovedAtUtc.ShouldBe(approvedAt);
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.Count.ShouldBe(2);
        ticket.ModificationHistory[^1].Action.ShouldBe(action);
    }

    [TestCase(TicketAction.Ban)]
    [TestCase(TicketAction.VoiceMute)]
    [TestCase(TicketAction.VoiceDeafen)]
    [TestCase(TicketAction.Role)]
    public void SetDuration_ApprovedTicket_PreservesApproval(TicketAction action)
    {
        // Arrange
        var ticket = TicketTests.Create(action);
        ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;
        var version = ticket.Version;

        // Act
        ticket.SetDuration(60, 3);

        // Assert
        ticket.DurationSeconds.ShouldBe(60);
        ticket.ExpiresAtUtc.ShouldBe(ticket.CreatedAtUtc.AddSeconds(60));
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ApprovedAtUtc.ShouldBe(approvedAt);
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.Count.ShouldBe(2);
        ticket.ModificationHistory[^1].Action.ShouldBe(TicketHistoryAction.SetDuration);
    }

    [Test]
    public void BeginTimeoutChange_ApprovedTicket_PreservesApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;
        var version = ticket.Version;
        var operation = new TicketOperation
        {
            Kind = "SetTimeoutExpiry",
            ExpiryUtc = ticket.CreatedAtUtc.AddHours(2),
            RequestedBy = 3,
        };

        // Act
        ticket.BeginTimeoutChange(operation);

        // Assert
        ticket.Pending.ShouldBeSameAs(operation);
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ApprovedAtUtc.ShouldBe(approvedAt);
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.ShouldHaveSingleItem().Action.ShouldBe(TicketHistoryAction.Approved);
    }

    [Test]
    public void TimeoutChange_ConfirmedOrCleared_PreservesApproval([Values] bool confirmed)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;
        var expiry = ticket.CreatedAtUtc.AddHours(2);
        ticket.BeginTimeoutChange(new TicketOperation
        {
            Kind = "SetTimeoutExpiry",
            ExpiryUtc = expiry,
            RequestedBy = 3,
        });

        // Act
        if (confirmed)
            ticket.SetDuration(7200, 3, observedExpiry: expiry);

        ticket.Pending = null;

        // Assert
        ticket.Pending.ShouldBeNull();
        ticket.DurationSeconds.ShouldBe(confirmed ? 7200 : 3600);
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ApprovedAtUtc.ShouldBe(approvedAt);
    }

    [Test]
    public void Approve_AfterEdit_PreservesOriginalApproval([Values] bool anotherReviewer)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;
        var approvalHistoryTimestamp = ticket.ModificationHistory[0].Timestamp;
        ticket.SetComment("Changed", 3);
        var version = ticket.Version;
        var reviewer = new TicketActor(1, anotherReviewer ? 5UL : 4UL, true, false);

        // Act
        ticket.Approve(reviewer, ticket.Version);

        // Assert
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ApprovedAtUtc.ShouldBe(approvedAt);
        ticket.Version.ShouldBe(version);
        ticket.ModificationHistory.Count.ShouldBe(2);
        ticket.ModificationHistory[0].Action.ShouldBe(TicketHistoryAction.Approved);
        ticket.ModificationHistory[0].Timestamp.ShouldBe(approvalHistoryTimestamp);
        ticket.ModificationHistory[1].Action.ShouldBe(TicketHistoryAction.SetComment);
    }

    [Test]
    public void Content_UnapprovedTicketEdited_RemainsUnapproved([Values] bool duration)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);

        // Act
        if (duration)
            ticket.SetDuration(60, 3);
        else
            ticket.SetComment("Changed", 3);

        // Assert
        ticket.ApprovedByUserId.ShouldBeNull();
        ticket.ApprovedAtUtc.ShouldBeNull();
    }

    [Test]
    public void Content_UnchangedThenExpired_PreservesApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);

        // Act
        ticket.SetComment("reason", 4);
        ticket.SetDuration(null, 4);
        ticket.End(TicketStatus.Expired);

        // Assert
        ticket.ApprovedByUserId.ShouldBe(4UL);
    }

    [Test]
    public void Content_ExemptTicketEdited_PreservesExemption()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban, reviewExempt: true);

        // Act
        ticket.SetComment("Changed", 4);

        // Assert
        ticket.ReviewExempt.ShouldBeTrue();
    }

    [Test]
    public void CanCorrect_SeniorModerator_CanEditEveryStoredType([Values] TicketAction action)
    {
        // Arrange
        var ticket = TicketTests.Create(action);

        // Act
        var allowed = ticket.CanCorrect(new TicketActor(1, 4, false, false, CanViewAuditLog: true));

        // Assert
        allowed.ShouldBeTrue();
    }
}
