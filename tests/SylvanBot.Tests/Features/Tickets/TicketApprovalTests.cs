using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketApprovalTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void AcceptCreation_ReviewerPermission_DeterminesAcceptance(bool canReview)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);

        // Act
        ticket.AcceptCreation(canReview);

        // Assert
        ticket.ApprovedAtUtc.ShouldBe(canReview ? ticket.CreatedAtUtc : null);
        ticket.ApprovedByUserId.ShouldBe(canReview ? ticket.ModeratorUserId : null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ConfirmTimeoutChange_DurationEdit_UsesReviewerPermission(bool canReview)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);

        ticket.Approve(
            new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: false, CanTimeoutMembers: false, CanViewAuditLog: true),
            ticket.Version);

        DateTimeOffset? acceptedAt = ticket.ApprovedAtUtc;

        ticket.BeginTimeoutChange(
            new TicketOperation { Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = ticket.CreatedAtUtc.AddDays(60), RequestedBy = 3 });

        // Act
        ticket.ConfirmTimeoutChange(ticket.CreatedAtUtc, canReview);

        // Assert
        ticket.ApprovedAtUtc.ShouldBe(canReview ? acceptedAt : null);
        ticket.ExpiresAtUtc.ShouldBe(ticket.CreatedAtUtc.AddDays(60));
        ticket.Pending.ShouldBeNull();
    }

    [Test]
    public void ConfirmTimeoutChange_AutomaticNativeRenewal_PreservesApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.SetDuration(60 * 86400, moderatorId: 3);
        ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);

        DateTimeOffset? acceptedAt = ticket.ApprovedAtUtc;
        var now = ticket.CreatedAtUtc.AddDays(21);

        ticket.BeginTimeoutChange(
            new TicketOperation
            {
                Kind = TicketOperationKind.SetTimeoutExpiry,
                ExpiryUtc = ticket.ExpiresAtUtc,
                NativeExpiryUtc = Ticket.NativeTimeoutExpiry(ticket.ExpiresAtUtc!.Value, now),
            });

        // Act
        ticket.ConfirmTimeoutChange(now);

        // Assert
        ticket.ApprovedAtUtc.ShouldBe(acceptedAt);
        ticket.ExpiresAtUtc.ShouldBe(ticket.CreatedAtUtc.AddDays(60));
        ticket.AppliedTimeoutUntilUtc!.Value.ShouldBeLessThan(ticket.ExpiresAtUtc!.Value);
    }

    [TestCase("✅", arg2: false, arg3: true)]
    [TestCase("✅\uFE0F", arg2: false, arg3: true)]
    [TestCase("👍", arg2: false, arg3: true)]
    [TestCase("ratified", arg2: true, arg3: true)]
    [TestCase("ratified", arg2: false, arg3: false)]
    [TestCase("white_check_mark", arg2: true, arg3: false)]
    [TestCase("thumbsup", arg2: true, arg3: false)]
    [TestCase("❌", arg2: false, arg3: false)]
    public void IsApprovalEmoji_OnlyRequestedEmoji_AcceptsReaction(string name, bool custom, bool expected)
    {
        // Arrange
        // Act
        var accepted = TicketReactionGatewayHandler.IsApprovalEmoji(name, custom);

        // Assert
        accepted.ShouldBe(expected);
    }

    [Test]
    public void TryApproveReaction_DefinitiveMessage_RecordsFirstReviewerOnly()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Publications.Add(new TicketPublication { ChannelId = 10, MessageId = 20, Current = true, Revision = ticket.Version });
        var first = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: false, CanTimeoutMembers: false, CanViewAuditLog: true);
        var second = new TicketActor(GuildId: 1, UserId: 5, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        var approved = ticket.TryApproveReaction(first, channelId: 10, messageId: 20);
        var repeated = ticket.TryApproveReaction(second, channelId: 10, messageId: 20);

        // Assert
        approved.ShouldBeTrue();
        repeated.ShouldBeFalse();
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ModificationHistory.ShouldHaveSingleItem().Action.ShouldBe(TicketHistoryAction.Approved);
    }

    [TestCase(10UL, 20UL, false, false)]
    [TestCase(10UL, 20UL, true, true)]
    [TestCase(10UL, 21UL, true, false)]
    [TestCase(11UL, 20UL, true, false)]
    public void TryApproveReaction_OldOrDifferentMessage_DoesNothing(ulong channel, ulong message, bool current, bool removed)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Publications.Add(new TicketPublication { ChannelId = 10, MessageId = 20, Current = current, Removed = removed });

        var reviewer = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        var approved = ticket.TryApproveReaction(reviewer, channel, message);

        // Assert
        approved.ShouldBeFalse();
        ticket.ApprovedByUserId.ShouldBeNull();
        ticket.ModificationHistory.ShouldBeEmpty();
    }

    [Test]
    public void TryApproveReaction_CommunityModerator_UsesExistingApprovalPermissions()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Publications.Add(new TicketPublication { ChannelId = 10, MessageId = 20, Current = true });

        var moderator = new TicketActor(GuildId: 1, UserId: 3, IsAdministrator: false, CanTimeoutMembers: true, CanBanMembers: true);

        // Act
        void Act()
        {
            ticket.TryApproveReaction(moderator, channelId: 10, messageId: 20);
        }

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.ApprovedByUserId.ShouldBeNull();
    }

    [Test]
    public void TryApproveReaction_UnpublishedEdit_RejectsApprovalOfUnseenChanges()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Publications.Add(new TicketPublication { ChannelId = 10, MessageId = 20, Current = true, Revision = ticket.Version });
        ticket.SetComment("new reason", moderatorId: 3);

        var reviewer = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false);

        // Act
        void Act()
        {
            ticket.TryApproveReaction(reviewer, channelId: 10, messageId: 20);
        }

        // Assert
        Should.Throw<BotCommandException>(Act);
        ticket.ApprovedByUserId.ShouldBeNull();
    }

    [Test]
    public void ReviewLineLinksOnlyTheNumberAndRetainsTheActionAndBothMentions()
    {
        var ticket = new Ticket
        {
            GuildId = 1,
            Number = 42,
            Action = TicketAction.Ban,
            SubjectUserId = 100,
            ModeratorUserId = 200,
        };

        TicketReviewSummary.ReviewLine(ticket, new TicketMessage(ChannelId: 10, MessageId: 20))
            .ShouldBe("- [#42](https://discord.com/channels/1/10/20) - <@100> banned by <@200>");

        TicketReviewSummary.ReviewLine(ticket, Optional<TicketMessage>.None)
            .ShouldBe("- #42 - <@100> banned by <@200>");
    }

    [Test]
    public void AwaitingApproval_InSameGuild_OmitsAcceptedTicketsAndNotes()
    {
        var live = new Ticket { GuildId = 1, Action = TicketAction.Timeout };
        var archive = new Ticket { GuildId = 1, Action = TicketAction.Ban };

        IQueryable<Ticket> pending = new[]
        {
            archive,
            live,
            new Ticket { GuildId = 1, Action = TicketAction.Note },
            TicketTests.Create(TicketAction.Ban, canReview: true),
            new Ticket { GuildId = 2, Action = TicketAction.Ban },
        }.AsQueryable();

        TicketCoordinator.AwaitingApproval(pending, guildId: 1)
            .ToArray()
            .ShouldBe(new[] { archive, live });
    }

    [Test]
    public void Approve_SeniorReviewer_RecordsOnlyOneApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        var reviewer = new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: false, CanTimeoutMembers: false, CanViewAuditLog: true);

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
        var moderator = new TicketActor(GuildId: 1, UserId: 3, IsAdministrator: false, CanTimeoutMembers: true, CanBanMembers: true);

        // Act
        void Act()
        {
            ticket.Approve(moderator, ticket.Version);
        }

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
        void Act()
        {
            ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version + 1);
        }

        // Assert
        Should.Throw<BotCommandException>(Act);
    }

    [Test]
    public void Approve_IncompleteOrPending_RejectsApproval([Values] bool pending)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);

        if (pending)
            ticket.Pending = new TicketOperation { Kind = TicketOperationKind.RemoveBan };
        else
            ticket.Initialise("", seconds: null);

        // Act
        void Act()
        {
            ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);
        }

        // Assert
        Should.Throw<BotCommandException>(Act);
    }

    [Test]
    public void SetComment_ApprovedTicket_PreservesApproval([Values] bool append)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);

        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;

        var version = ticket.Version;
        var comment = append ? "reason\nAdditional detail" : "Changed";
        var action = append ? TicketHistoryAction.AppendedComment : TicketHistoryAction.SetComment;

        // Act
        ticket.SetComment(comment, moderatorId: 3, append);

        // Assert
        ticket.Comment.ShouldBe(comment);
        ticket.ApprovedByUserId.ShouldBe(4UL);
        ticket.ApprovedAtUtc.ShouldBe(approvedAt);
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.Count.ShouldBe(2);
        ticket.ModificationHistory[^1].Action.ShouldBe(action);
    }

    [TestCase(TicketAction.Ban, arg2: true)]
    [TestCase(TicketAction.Ban, arg2: false)]
    [TestCase(TicketAction.VoiceMute, arg2: true)]
    [TestCase(TicketAction.VoiceMute, arg2: false)]
    [TestCase(TicketAction.VoiceDeafen, arg2: true)]
    [TestCase(TicketAction.VoiceDeafen, arg2: false)]
    [TestCase(TicketAction.Role, arg2: true)]
    [TestCase(TicketAction.Role, arg2: false)]
    public void SetDuration_ApprovedTicket_UsesEditorPermission(TicketAction action, bool canReview)
    {
        // Arrange
        var ticket = TicketTests.Create(action);
        ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;

        var version = ticket.Version;

        // Act
        ticket.SetDuration(seconds: 60, moderatorId: 3, canReview: canReview);

        // Assert
        ticket.DurationSeconds.ShouldBe(60);
        ticket.ExpiresAtUtc.ShouldBe(ticket.CreatedAtUtc.AddSeconds(60));
        ticket.ApprovedByUserId.ShouldBe(canReview ? 4UL : null);
        ticket.ApprovedAtUtc.ShouldBe(canReview ? approvedAt : null);
        ticket.Version.ShouldBe(version + 1);
        ticket.ModificationHistory.Count.ShouldBe(2);
        ticket.ModificationHistory[^1].Action.ShouldBe(TicketHistoryAction.SetDuration);
    }

    [Test]
    public void BeginTimeoutChange_ApprovedTicket_PreservesApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;

        var version = ticket.Version;

        var operation = new TicketOperation
        {
            Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = ticket.CreatedAtUtc.AddHours(2), RequestedBy = 3,
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
    public void TimeoutChange_ConfirmedOrdinaryEdit_RequiresApproval([Values] bool confirmed)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Timeout);
        ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;
        var expiry = ticket.CreatedAtUtc.AddHours(2);

        ticket.BeginTimeoutChange(
            new TicketOperation { Kind = TicketOperationKind.SetTimeoutExpiry, ExpiryUtc = expiry, RequestedBy = 3 });

        // Act
        if (confirmed)
            ticket.SetDuration(seconds: 7200, moderatorId: 3, expiry);

        ticket.Pending = null;

        // Assert
        ticket.Pending.ShouldBeNull();
        ticket.DurationSeconds.ShouldBe(confirmed ? 7200 : 3600);
        ticket.ApprovedByUserId.ShouldBe(confirmed ? null : 4UL);
        ticket.ApprovedAtUtc.ShouldBe(confirmed ? null : approvedAt);
    }

    [Test]
    public void Approve_AfterEdit_PreservesOriginalApproval([Values] bool anotherReviewer)
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);
        DateTimeOffset? approvedAt = ticket.ApprovedAtUtc;

        var approvalHistoryTimestamp = ticket.ModificationHistory[0].Timestamp;
        ticket.SetComment("Changed", moderatorId: 3);

        var version = ticket.Version;
        var reviewer = new TicketActor(GuildId: 1, anotherReviewer ? 5UL : 4UL, IsAdministrator: true, CanTimeoutMembers: false);

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
            ticket.SetDuration(seconds: 60, moderatorId: 3, canReview: true);
        else
            ticket.SetComment("Changed", moderatorId: 3);

        // Assert
        ticket.ApprovedByUserId.ShouldBeNull();
        ticket.ApprovedAtUtc.ShouldBeNull();
    }

    [Test]
    public void Content_UnchangedThenExpired_PreservesApproval()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban);
        ticket.Approve(new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: true, CanTimeoutMembers: false), ticket.Version);

        // Act
        ticket.SetComment("reason", moderatorId: 4);
        ticket.SetDuration(seconds: null, moderatorId: 4);
        ticket.End(TicketStatus.Expired);

        // Assert
        ticket.ApprovedByUserId.ShouldBe(4UL);
    }

    [Test]
    public void Content_AutomaticallyAcceptedTicketEdited_PreservesAcceptance()
    {
        // Arrange
        var ticket = TicketTests.Create(TicketAction.Ban, canReview: true);

        // Act
        ticket.SetComment("Changed", moderatorId: 4);

        // Assert
        ticket.ApprovedAtUtc.ShouldNotBeNull();
    }

    [Test]
    public void CanCorrect_SeniorModerator_CanEditEveryStoredType([Values] TicketAction action)
    {
        // Arrange
        var ticket = TicketTests.Create(action);

        // Act
        var allowed = ticket.CanCorrect(
            new TicketActor(GuildId: 1, UserId: 4, IsAdministrator: false, CanTimeoutMembers: false, CanViewAuditLog: true));

        // Assert
        allowed.ShouldBeTrue();
    }
}