namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketApprovalTests
{
    [Test]
    public void Approve_Reviewer_RecordsApproval()
    {
        // Arrange
        var ticket = CompleteTicket();
        var reviewer = new TicketActor(1, 2, IsAdministrator: false, CanTimeoutMembers: false, CanViewAuditLog: true);

        // Act
        ticket.Approve(reviewer, 0);

        // Assert
        ticket.ApprovedByUserId.ShouldBe(2UL);
        ticket.ApprovedAtUtc.ShouldNotBeNull();
        ticket.Version.ShouldBe(1);
        ticket.Completion.ShouldBe(TicketCompletion.Complete);
    }

    [Test]
    public void Approve_OrdinaryModerator_RejectsApproval()
    {
        // Arrange
        var ticket = CompleteTicket();
        var moderator = new TicketActor(1, 2, IsAdministrator: false, CanTimeoutMembers: true, CanBanMembers: true);

        // Act
        void Act() => ticket.Approve(moderator, 0);

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.ApprovedAtUtc.ShouldBeNull();
    }

    [Test]
    public void Approve_StaleVersion_RejectsApproval()
    {
        // Arrange
        var ticket = CompleteTicket();
        var reviewer = new TicketActor(1, 2, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        void Act() => ticket.Approve(reviewer, 1);

        // Assert
        Should.Throw<BotCommandException>(Act);
    }

    [Test]
    public void Approve_IncompleteOrPending_RejectsApproval([Values] bool pendingOperation)
    {
        // Arrange
        var ticket = CompleteTicket();
        var reviewer = new TicketActor(1, 2, IsAdministrator: true, CanTimeoutMembers: false);

        if (pendingOperation)
            ticket.PendingOperationId = Guid.NewGuid();
        else
            ticket.Completion = TicketCompletion.Pending;

        // Act
        void Act() => ticket.Approve(reviewer, 0);

        // Assert
        Should.Throw<BotCommandException>(Act);
    }

    [Test]
    public void Content_Changed_ResetsApproval([Values] bool duration)
    {
        // Arrange
        var ticket = CompleteTicket();
        ticket.Approve(new TicketActor(1, 2, IsAdministrator: true, CanTimeoutMembers: false), 0);

        // Act
        if (duration)
            ticket.DurationSeconds = 60;
        else
            ticket.Comment = "Updated reason";

        // Assert
        ticket.ApprovedAtUtc.ShouldBeNull();
        ticket.ApprovedByUserId.ShouldBeNull();
    }

    [Test]
    public void Content_UnchangedAndExpired_PreservesApproval()
    {
        // Arrange
        var ticket = CompleteTicket();
        ticket.Approve(new TicketActor(1, 2, IsAdministrator: true, CanTimeoutMembers: false), 0);

        // Act
        ticket.Comment = "Reason";
        ticket.DurationSeconds = null;
        ticket.Status = TicketStatus.Expired;

        // Assert
        ticket.ApprovedAtUtc.ShouldNotBeNull();
    }

    private static Ticket CompleteTicket() => new Ticket
    {
        GuildId = 1,
        Action = TicketAction.Ban,
        Comment = "Reason",
        DurationKnown = true,
        Completion = TicketCompletion.Complete,
        Status = TicketStatus.Active,
    };
}
