namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketDetailsTests
{
    [Test]
    public void ParseReply_MultipleDurationItems_SeparatesComment()
    {
        // Arrange
        const string reply = "1 day 8 hours, breaking rules";

        // Act
        var details = TicketDetails.ParseReply(reply);

        // Assert
        details.HasDuration.ShouldBeTrue();
        details.DurationSeconds.ShouldBe(115200);
        details.Comment.ShouldBe("breaking rules");
    }

    [TestCase("p, repeated spam")]
    [TestCase("PERM repeated spam")]
    [TestCase("permanent, repeated spam")]
    public void ParseReply_Permanent_KeepsExplicitDuration(string reply)
    {
        // Arrange
        // Act
        var details = TicketDetails.ParseReply(reply);

        // Assert
        details.HasDuration.ShouldBeTrue();
        details.DurationSeconds.ShouldBeNull();
        details.Comment.ShouldBe("repeated spam");
    }

    [Test]
    public void ParseReply_CommentOnly_LeavesDurationUnspecified()
    {
        // Arrange
        const string reply = "persistent spam";

        // Act
        var details = TicketDetails.ParseReply(reply);

        // Assert
        details.HasDuration.ShouldBeFalse();
        details.Comment.ShouldBe(reply);
    }

    [TestCase("0 hours, reason")]
    [TestCase("-1 day, reason")]
    [TestCase("1 day -1 hour, reason")]
    [TestCase("1 day 8 elephants, reason")]
    [TestCase("10, reason")]
    [TestCase("١ day, reason")]
    [TestCase("9223372036854775807 years, reason")]
    [TestCase(" ")]
    public void ParseReply_InvalidDurationOrBlank_RejectsReply(string reply)
    {
        // Arrange
        // Act
        // Assert
        Should.Throw<BotCommandException>(() => TicketDetails.ParseReply(reply));
    }

    [Test]
    public void ParseReply_DurationOnly_LeavesCommentUnspecified()
    {
        // Arrange
        const string reply = "2 hours";

        // Act
        var details = TicketDetails.ParseReply(reply);

        // Assert
        details.DurationSeconds.ShouldBe(7200);
        details.Comment.ShouldBeNull();
    }

    [TestCase("1 m, reason", 60L)]
    [TestCase("1 M, reason", 2592000L)]
    [TestCase("1 month, reason", 2592000L)]
    [TestCase("1 year, reason", 31536000L)]
    public void ParseReply_LegacyUnits_PreservesDurationMeaning(string reply, long seconds)
    {
        // Arrange
        // Act
        var details = TicketDetails.ParseReply(reply);

        // Assert
        details.DurationSeconds.ShouldBe(seconds);
        details.Comment.ShouldBe("reason");
    }

    [Test]
    public void ParseReply_OversizedComment_RejectsReply()
    {
        // Arrange
        var reply = "1 hour, " + new string('a', Ticket.CommentLimit + 1);

        // Act
        // Assert
        Should.Throw<BotCommandException>(() => TicketDetails.ParseReply(reply));
    }

    [Test]
    public void ParseFields_DurationContainsComment_RejectsField()
    {
        // Arrange
        // Act
        // Assert
        Should.Throw<BotCommandException>(() => TicketDetails.ParseFields("1 day, comment", null));
    }

    [Test]
    public void Expiry_FiniteDuration_UsesActionTime()
    {
        // Arrange
        var actionTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var details = TicketDetails.ParseReply("2 days");

        // Act
        DateTimeOffset? expiry = details.Expiry(actionTime);

        // Assert
        expiry.ShouldBe(actionTime.AddDays(2));
    }

    [Test]
    public void TimeoutExpiry_Permanent_RejectsDuration()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var details = TicketDetails.ParseReply("permanent");

        // Act
        // Assert
        Should.Throw<BotCommandException>(() => details.TimeoutExpiry(now, now));
    }

    [Test]
    public void TimeoutExpiry_BeyondDiscordLimit_RejectsWithoutClamping()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var details = TicketDetails.ParseReply("29 days");

        // Act
        // Assert
        Should.Throw<BotCommandException>(() => details.TimeoutExpiry(now, now));
    }

    [Test]
    public void TimeoutExpiry_ElapsedDuration_ReturnsPastExpiry()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var details = TicketDetails.ParseReply("1 day");

        // Act
        var expiry = details.TimeoutExpiry(now.AddDays(-2), now);

        // Assert
        expiry.ShouldBe(now.AddDays(-1));
    }

    [Test]
    public void TimeoutExpiry_OldActionTime_AppliesLimitFromNow()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var details = TicketDetails.ParseReply("50 days");

        // Act
        var expiry = details.TimeoutExpiry(now.AddDays(-30), now);

        // Assert
        expiry.ShouldBe(now.AddDays(20));
    }
}
