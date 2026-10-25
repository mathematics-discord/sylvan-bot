namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public sealed class TicketTests
{
    [Test]
    public void Comment_Null_ClearsComment([Values] TicketAction action)
    {
        // Arrange
        var ticket = new Ticket { Action = action, Comment = "Original comment" };

        // Act
        ticket.Comment = null;

        // Assert
        ticket.Comment.ShouldBeNull();
    }

    [Test]
    public void Comment_AtLimit_AcceptsComment([Values] TicketAction action)
    {
        // Arrange
        var comment = new string('a', 3000);

        // Act
        var ticket = new Ticket { Action = action, Comment = comment };

        // Assert
        ticket.Comment.ShouldBe(comment);
    }

    [Test]
    public void Comment_OverLimit_ThrowsWithoutChangingComment([Values] TicketAction action)
    {
        // Arrange
        var ticket = new Ticket { Action = action, Comment = "Original comment" };
        var comment = new string('a', 3001);

        // Act
        Should.Throw<ArgumentOutOfRangeException>(() => ticket.Comment = comment);

        // Assert
        ticket.Comment.ShouldBe("Original comment");
    }
}