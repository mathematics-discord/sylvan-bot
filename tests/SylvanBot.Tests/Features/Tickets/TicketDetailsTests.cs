namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketDetailsTests
{
    [TestCase("604802 seconds", 604802L, false)]
    [TestCase("1 hour", 3600L, false)]
    [TestCase("60 minutes", 3600L, false)]
    [TestCase("2 hours", 3600L, true)]
    [TestCase("1 hour", 7200L, true)]
    [TestCase("permanent", null, false)]
    [TestCase("permanent", 3600L, true)]
    [TestCase("1 hour", null, true)]
    [TestCase(null, 3600L, false)]
    [TestCase(null, null, false)]
    public void ChangesDuration_CommentEdit_RequiresDifferentDuration(string? duration, long? currentSeconds, bool expected)
    {
        // Arrange
        var details = TicketDetails.ParseFields(duration, "updated comment");

        // Act
        var changesDuration = details.ChangesDuration(currentSeconds);

        // Assert
        changesDuration.ShouldBe(expected);
        details.Comment.Apply("old comment").ShouldBe("updated comment");
    }

    [Test]
    public void PermanentDuration_Expiry_DoesNotOverflow()
    {
        // Arrange
        var details = TicketDetails.ParseFields("permanent", "reason");
        var actionTime = DateTimeOffset.UtcNow;

        // Act
        var expiry = details.Expiry(actionTime);

        // Assert
        details.DurationSeconds.ShouldBe(long.MaxValue);
        expiry.ShouldBe(DateTimeOffset.MaxValue);
        Should.Throw<BotCommandException>(() => details.TimeoutExpiry(actionTime, actionTime));

    }

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
        details.Comment.Apply(string.Empty).ShouldBe("breaking rules");
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
        details.DurationSeconds.ShouldBe(long.MaxValue);
        details.Comment.Apply(string.Empty).ShouldBe("repeated spam");
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
        details.Comment.Apply(string.Empty).ShouldBe(reply);
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
        (details.Comment is TicketCommentEdit.Keep).ShouldBeTrue();
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
        details.Comment.Apply(string.Empty).ShouldBe("reason");
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
        var expiry = details.Expiry(actionTime);

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
    public void TimeoutExpiry_BeyondDiscordLimit_PreservesFullDuration()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var details = TicketDetails.ParseReply("29 days");

        // Act
        var expiry = details.TimeoutExpiry(now, now);

        // Assert
        expiry.ShouldBe(now.AddDays(29));
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
    public void TimeoutExpiry_OldActionTime_UsesOriginalActionTime()
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
