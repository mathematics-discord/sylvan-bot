using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketAutomodApprovalTests
{
    [TestCase(TicketAction.Ban)]
    [TestCase(TicketAction.Timeout)]
    public void AcceptCreation_AutomodAction_RequiresApprovalDespiteReviewerPermission(TicketAction action)
    {
        // Arrange
        var ticket = TicketTests.Create(action);

        // Act
        ticket.AcceptCreation(canReview: true, requiresApproval: true);

        // Assert
        ticket.ApprovedAtUtc.ShouldBeNull();
        ticket.ApprovedByUserId.ShouldBeNull();

        TicketCoordinator.AwaitingApproval(new[] { ticket }.AsQueryable(), ticket.GuildId)
            .ShouldHaveSingleItem()
            .ShouldBeSameAs(ticket);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AcceptCreation_Note_NeverEntersReview(bool requiresApproval)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Note);

        // Act
        ticket.AcceptCreation(canReview: true, requiresApproval);

        // Assert
        TicketCoordinator.AwaitingApproval(new[] { ticket }.AsQueryable(), ticket.GuildId)
            .ShouldBeEmpty();
    }

    [Test]
    public void SetDuration_AutomodExtension_ReturnsAcceptedTimeoutToReview()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout, canReview: true);

        // Act
        ticket.SetDuration(seconds: 7200, moderatorId: null, canReview: true, requiresApproval: true);

        // Assert
        ticket.ApprovedAtUtc.ShouldBeNull();
        ticket.ApprovedByUserId.ShouldBeNull();
        ticket.DurationSeconds.ShouldBe(7200);
    }
}