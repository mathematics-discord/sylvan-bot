using SylvanBot.Application.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketModeratorTests
{
    [TestCase(null, true)]
    [TestCase(3, true)]
    [TestCase(4, false)]
    public void NeedsPromptRefresh_SavedRevision_RefreshesOnlyChangedPrompt(int? displayed, bool expected)
    {
        // Arrange
        var moderator = new TicketModerator { PromptRevision = displayed };

        // Act
        var refresh = moderator.NeedsPromptRefresh(4);

        // Assert
        refresh.ShouldBe(expected);
    }

    [Test]
    public void ClearDelivery_PreviousPrompt_DiscardsItsRevision()
    {
        // Arrange
        var moderator = new TicketModerator
        {
            ActiveTicketId = 10,
            PromptMessageId = 20,
            PromptRevision = 4,
            NextDeliveryUtc = DateTimeOffset.UtcNow.AddDays(1),
        };

        // Act
        moderator.ClearDelivery();

        // Assert
        moderator.PromptRevision.ShouldBeNull();
        moderator.ActiveTicketId.ShouldBeNull();
        moderator.PromptMessageId.ShouldBeNull();
        moderator.NeedsPromptRefresh(4).ShouldBeTrue();
        moderator.Version.ShouldBe(1);
    }

    [Test]
    public void IsDeliveryDue_UnchangedPrompt_PreservesReminderDeadline()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var moderator = new TicketModerator { ActiveTicketId = 10, PromptRevision = 4, NextDeliveryUtc = now.AddHours(1) };

        // Act
        var due = moderator.IsDeliveryDue(10, now);

        // Assert
        due.ShouldBeFalse();
        moderator.IsDeliveryDue(11, now).ShouldBeTrue();
        moderator.IsDeliveryDue(10, now.AddHours(1)).ShouldBeTrue();
    }
}