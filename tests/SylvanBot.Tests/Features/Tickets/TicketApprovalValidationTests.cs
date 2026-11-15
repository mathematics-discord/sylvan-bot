using SylvanBot.Application.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketApprovalValidationTests
{
    [Test]
    public void Approve_ExpiredTimeoutWithoutComment_ExplainsHowToCompleteIt()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.Initialise(string.Empty, seconds: 3600);
        ticket.End(TicketStatus.Expired);
        var reviewer = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        var exception = Should.Throw<BotCommandException>(() => ticket.Approve(reviewer, ticket.Version));

        // Assert
        exception.Message.ShouldBe("Add a comment using Edit before approving this ticket.");
        ticket.IsApproved.ShouldBeFalse();
    }

    [Test]
    public void Approve_CompletedExpiredTimeout_AcceptsRecord()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.Initialise(string.Empty, seconds: 3600);
        ticket.End(TicketStatus.Expired);
        var reviewer = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false);
        ticket.SetComment("Reason supplied after the timeout ended", reviewer.UserId);

        // Act
        ticket.Approve(reviewer, ticket.Version);

        // Assert
        ticket.IsApproved.ShouldBeTrue();
        ticket.Status.ShouldBe(TicketStatus.Expired);
        ticket.ApprovedByUserId.ShouldBe(reviewer.UserId);
    }

    [Test]
    public void Approve_StaleIncompleteTicket_RequestsFreshView()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.Initialise(string.Empty, seconds: 3600);
        var version = ticket.Version;
        ticket.End(TicketStatus.Expired);
        var reviewer = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        var exception = Should.Throw<BotCommandException>(() => ticket.Approve(reviewer, version));

        // Assert
        exception.Message.ShouldBe("This ticket changed. Reopen it before approving.");
        ticket.IsApproved.ShouldBeFalse();
    }

    [TestCase(TicketAction.Timeout)]
    [TestCase(TicketAction.Role)]
    [TestCase(TicketAction.VoiceMute)]
    [TestCase(TicketAction.VoiceDeafen)]
    public void CompleteStaffReversal_HideAndRestore_DoesNotReactivateRestriction(TicketAction action)
    {
        // Arrange
        var ticket = TicketTests.Create(action);
        var reviewer = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        ticket.CompleteStaffReversal(reviewer.UserId, DateTimeOffset.UtcNow);
        ticket.SetHidden(reviewer, ticket.Version, hidden: true);
        ticket.SetHidden(reviewer, ticket.Version, hidden: false);

        // Assert
        ticket.Status.ShouldBe(TicketStatus.Reverted);
        ticket.IsApproved.ShouldBeTrue();
        ticket.Hidden.ShouldBeFalse();
        ticket.NeedsTimeoutRenewal(DateTimeOffset.UtcNow).ShouldBeFalse();
        ticket.Pending.ShouldBeNull();
    }

}