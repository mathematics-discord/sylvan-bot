namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketApprovalTests
{
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
    public void Content_Changed_InvalidatesApproval([Values] bool duration)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Approve(new TicketActor(1, 4, true, false), ticket.Version);

        // Act
        if (duration)
            ticket.SetDuration(60, 4);
        else ticket.SetComment("Changed", 4);

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
