using Microsoft.EntityFrameworkCore;
using RatBot.Features.Tickets;
using RatBot.Tests.Features.Tickets;

namespace RatBot.Tests.Integration.Tickets;

[TestFixture]
[NonParallelizable]
public sealed class TicketWorkflowTests
{
    [SetUp]
    public async Task ResetAsync()
    {
        await using var dbContext = PostgresDatabaseFixture.CreateDbContext();
        await dbContext.Tickets
            .ExecuteDeleteAsync();

        await dbContext.TicketConfigurations
            .ExecuteDeleteAsync();

        await dbContext.Database.ExecuteSqlRawAsync("ALTER SEQUENCE ticket_numbers RESTART WITH 1");
    }

    [Test]
    public async Task ConfigureAsync_DefaultsDisabledAndRequiresAdministrator()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();

        // Act
        var configuration = await fixture.Workflow.ConfigureAsync(TicketTestFixture.Administrator, null, null);

        // Assert
        configuration.Enabled.ShouldBeFalse();
        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            fixture.Workflow.ConfigureAsync(TicketTestFixture.Moderator, true, 200));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConfigureAsync_DisableWithUnavailableChannel_SavesWithoutCheckingDestination(bool missing)
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        fixture.Discord.ChannelMissing = missing;
        fixture.Discord.DenyPublication = !missing;

        // Act
        var configuration = await fixture.Workflow.ConfigureAsync(TicketTestFixture.Administrator, false, null);

        // Assert
        configuration.Enabled.ShouldBeFalse();
        configuration.DestinationChannelId.ShouldBe(200UL);
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var saved = await dbContext.TicketConfigurations
            .SingleAsync();

        saved.Enabled.ShouldBeFalse();
    }

    [TestCase(true, null)]
    [TestCase(false, 201UL)]
    public async Task ConfigureAsync_EnableOrSelectDestination_RequiresPublishingAccess(bool enabled, ulong? channelId)
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        await fixture.Workflow.ConfigureAsync(TicketTestFixture.Administrator, false, null);
        fixture.Discord.DenyPublication = true;

        // Act
        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            fixture.Workflow.ConfigureAsync(TicketTestFixture.Administrator, enabled, channelId));

        // Assert
        var configuration = await fixture.Workflow.ConfigureAsync(TicketTestFixture.Administrator, null, null);
        configuration.Enabled.ShouldBeFalse();
        configuration.DestinationChannelId.ShouldBe(200UL);
    }

    [Test]
    public async Task CreateAsync_ConcurrentWorkflows_AllocateDistinctPositiveNumbers()
    {
        // Arrange
        await using var first = new TicketTestFixture();
        await using var second = new TicketTestFixture();
        await first.EnableAsync();

        // Act
        var changes = await Task.WhenAll(
            first.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "First"),
            second.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "Second"));

        // Assert
        changes[0].Ticket.Number.ShouldBeGreaterThan(0);
        changes[1].Ticket.Number.ShouldBeGreaterThan(0);
        changes[0].Ticket.Number.ShouldNotBe(changes[1].Ticket.Number);
    }

    [Test]
    public async Task EditAndHide_SameNumberInDifferentGuilds_ChangeOnlyTheActorsGuild()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        await fixture.StoreAsync(3);
        await fixture.StoreAsync(3, guildId: 999, comment: "Other guild");

        // Act
        var opened = await fixture.Workflow.PrepareEditAsync(TicketTestFixture.Moderator, 3);
        await fixture.Workflow.SetAsync(TicketTestFixture.Moderator, 3,
            comment: "Edited", expectedVersion: opened.Version);
        await fixture.Workflow.HideAsync(TicketTestFixture.Moderator, 3);

        // Assert
        (await fixture.Workflow.ShowAsync(TicketTestFixture.Moderator, 3)).Value.ShouldBeOfType<None>();
        var hidden = await fixture.Workflow.ShowAsync(TicketTestFixture.Moderator, 3, hidden: true);
        hidden.Value.ShouldBeOfType<Some<Ticket>>().Value.Comment.ShouldBe("Edited");
        var foreign = await fixture.Workflow.ShowAsync(TicketTestFixture.Moderator with { GuildId = 999 }, 3);
        foreign.Value.ShouldBeOfType<Some<Ticket>>().Value.Comment.ShouldBe("Other guild");
    }

    [TestCase("edit")]
    [TestCase("submit")]
    [TestCase("hide")]
    public async Task Mutation_ChannelAccessRevoked_DoesNotChangeTicket(string operation)
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        await fixture.StoreAsync(3);
        fixture.Discord.DenyRead = true;

        // Act
        Task Act() => operation switch
        {
            "edit" => fixture.Workflow.PrepareEditAsync(TicketTestFixture.Moderator, 3),
            "submit" => fixture.Workflow.SetAsync(TicketTestFixture.Moderator, 3, comment: "Denied", expectedVersion: 0),
            _ => fixture.Workflow.HideAsync(TicketTestFixture.Moderator, 3),
        };

        await Should.ThrowAsync<UnauthorizedAccessException>(Act);

        // Assert
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var ticket = await dbContext.Tickets
            .SingleAsync();

        ticket.Comment.ShouldBe("Stored comment");
        ticket.Hidden.ShouldBeFalse();
        ticket.Version.ShouldBe(0);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateAsync_AuthorisedModerator_SavesFullCommentBeforePublishing(bool administrator)
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        var actor = administrator ? TicketTestFixture.Administrator : TicketTestFixture.Moderator;
        var comment = "  @everyone <@123>\n" + new string('x', 5000);
        fixture.Discord.BeforePublish = async ticket =>
        {
            await using var dbContext = await fixture.Factory.CreateDbContextAsync();
            var saved = await dbContext.Tickets
                .SingleAsync();

            saved.Comment.ShouldBe(comment);
            saved.MessageId.ShouldBeNull();
            saved.PublicationPending.ShouldBeTrue();
        };

        // Act
        var change = await fixture.Workflow.CreateAsync(actor, 123, comment);

        // Assert
        change.Ticket.Number.ShouldBe(1);
        change.Ticket.ModeratorUserId.ShouldBe(actor.UserId);
        change.Ticket.DurationSeconds.ShouldBeNull();
        change.ReferenceSaved.ShouldBeTrue();
        change.ConfirmedPublication.ShouldNotBeNull();
    }

    [TestCase("permission")]
    [TestCase("mute-only")]
    [TestCase("disabled")]
    [TestCase("guild")]
    [TestCase("channel")]
    [TestCase("publish-permission")]
    public async Task CreateAsync_FailedPreconditions_DoesNotSaveOrPublish(string failure)
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        var actor = TicketTestFixture.Moderator;

        switch (failure)
        {
            case "permission": actor = actor with { CanTimeoutMembers = false }; break;
            case "mute-only": actor = actor with { CanTimeoutMembers = false, CanMuteMembers = true }; break;
            case "guild": actor = actor with { GuildId = 999 }; break;
            case "channel": fixture.Discord.DenyRead = true; break;
            case "publish-permission": fixture.Discord.DenyPublication = true; break;
            case "disabled": await fixture.Workflow.ConfigureAsync(TicketTestFixture.Administrator, false, null); break;
        }

        // Act
        await Should.ThrowAsync<Exception>(() => fixture.Workflow.CreateAsync(actor, 123, "A note"));

        // Assert
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var count = await dbContext.Tickets
            .CountAsync();

        count.ShouldBe(0);
        fixture.Discord.Publications.ShouldBe(0);
    }

    [Test]
    public async Task CreateAsync_SaveFails_DoesNotPublish()
    {
        // Arrange
        await using var fixture = new TicketTestFixture(EntityState.Added);
        await fixture.EnableAsync();

        // Act
        await Should.ThrowAsync<DbUpdateException>(() =>
            fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "A note"));

        // Assert
        fixture.Discord.Publications.ShouldBe(0);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateAsync_PublicationOrReferenceSaveFails_RetainsConfirmedFacts(bool referenceSaveFails)
    {
        // Arrange
        await using var fixture = new TicketTestFixture(referenceSaveFails ? EntityState.Modified : null);
        await fixture.EnableAsync();
        fixture.Discord.PublicationFails = !referenceSaveFails;

        // Act
        var change = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "A note");

        // Assert
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var saved = await dbContext.Tickets
            .SingleAsync();

        saved.Comment.ShouldBe("A note");
        saved.PublicationPending.ShouldBeTrue();
        change.PublicationUncertain.ShouldBe(!referenceSaveFails);
        change.ReferenceSaved.ShouldBeFalse();
        (change.ConfirmedPublication is not null).ShouldBe(referenceSaveFails);
        fixture.Discord.Publications.ShouldBe(1);
    }

    [Test]
    public async Task SetAsync_UncertainPublication_DoesNotBlindlyRepost()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        fixture.Discord.PublicationFails = true;
        var created = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "Original");
        fixture.Discord.PublicationFails = false;

        // Act
        var corrected = await fixture.Workflow.SetAsync(
            TicketTestFixture.Moderator, created.Ticket.Number, comment: "Correction");

        // Assert
        corrected.PublicationUncertain.ShouldBeTrue();
        fixture.Discord.Publications.ShouldBe(1);
        (await fixture.Workflow.PrepareEditAsync(TicketTestFixture.Moderator, created.Ticket.Number))
            .Comment.ShouldBe("Correction");
    }

    [TestCase("123")]
    [TestCase("<@123>")]
    [TestCase("<@!123>")]
    public void ParseUser_DepartedUserInput_ReturnsId(string input)
    {
        // Arrange / Act
        var id = TicketReference.ParseUser(input);

        // Assert
        id.ShouldBe(123UL);
    }

    [TestCase("https://discord.com/channels/999/200/401")]
    [TestCase("https://example.com/channels/100/200/401")]
    public void Parse_ForeignLink_IsRejected(string input)
    {
        // Arrange / Act / Assert
        Should.Throw<ArgumentException>(() => TicketReference.ParseMessageLink(input, 100));
    }

    [Test]
    public async Task Reads_NumbersUsersAndHiddenViews_StayGuildScoped()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        await fixture.StoreAsync();
        await fixture.StoreAsync(32703, hidden: true);
        await fixture.StoreAsync(32702, guildId: 999, comment: "Other guild");
        var created = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "New");
        await fixture.Workflow.ConfigureAsync(TicketTestFixture.Administrator, false, null);

        // Act
        var numeric = await fixture.Workflow.ShowAsync(TicketTestFixture.Moderator, 32702);
        var numbered = await fixture.Workflow.ShowAsync(TicketTestFixture.Moderator, created.Ticket.Number);
        var hidden = await fixture.Workflow.ShowAsync(TicketTestFixture.Moderator, 32703, hidden: true);
        var normal = await fixture.Workflow.ListAsync(TicketTestFixture.Moderator, 123);
        var hiddenPage = await fixture.Workflow.ListAsync(TicketTestFixture.Moderator, 123, hidden: true);

        // Assert
        numeric.Value.ShouldBeOfType<Some<Ticket>>().Value.Comment.ShouldBe("Stored comment");
        numbered.Value.ShouldBeOfType<Some<Ticket>>().Value.Comment.ShouldBe("New");
        hidden.Value.ShouldBeOfType<Some<Ticket>>().Value.Hidden.ShouldBeTrue();
        (await fixture.Workflow.ShowAsync(TicketTestFixture.Moderator, 32703)).Value.ShouldBeOfType<None>();
        normal.Items.Length.ShouldBe(2);
        hiddenPage.Items.Length.ShouldBe(1);
        var foreign = await fixture.Workflow.ShowAsync(
            TicketTestFixture.Moderator with { GuildId = 999 }, created.Ticket.Number);
        foreign.Value.ShouldBeOfType<None>();
    }

    [Test]
    public async Task ListAsync_AllTypesAndStatuses_PaginatesWithoutDroppingExpiredOrReverted()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        var actions = Enum.GetValues<TicketAction>();

        for (var index = 0; index < 13; index++)
        {
            await fixture.StoreAsync(index + 1, actions[index % actions.Length],
                status: index % 2 == 0 ? TicketStatus.Expired : TicketStatus.Reverted,
                comment: new string('x', 5000));
        }

        // Act
        var first = await fixture.Workflow.ListAsync(TicketTestFixture.Moderator, 123);
        var second = await fixture.Workflow.ListAsync(TicketTestFixture.Moderator, 123, page: 2);

        // Assert
        first.Items.Length.ShouldBe(10);
        first.HasMore.ShouldBeTrue();
        second.Items.Length.ShouldBe(3);
        second.HasMore.ShouldBeFalse();
        first.Items[0].Comment!.Length.ShouldBe(5000);
    }

    [TestCase("set")]
    [TestCase("append")]
    [TestCase("edit")]
    [TestCase("hide")]
    public async Task Mutation_MuteOnlyOrReadOnlyType_IsRejected(string operation)
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        await fixture.StoreAsync();
        await fixture.StoreAsync(32703, TicketAction.Ban);
        var muteOnly = TicketTestFixture.Moderator with { CanTimeoutMembers = false, CanMuteMembers = true };

        Task Act(TicketActor actor, long number) => operation switch
        {
            "set" => fixture.Workflow.SetAsync(actor, number, comment: "Replacement"),
            "append" => fixture.Workflow.AppendAsync(actor, number, "More"),
            "edit" => fixture.Workflow.PrepareEditAsync(actor, number),
            _ => fixture.Workflow.HideAsync(actor, number),
        };

        // Act / Assert
        await Should.ThrowAsync<UnauthorizedAccessException>(() => Act(muteOnly, 32702));
        await Should.ThrowAsync<NotSupportedException>(() => Act(TicketTestFixture.Administrator, 32703));
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var unchanged = await dbContext.Tickets
            .AllAsync(ticket => ticket.Version == 0 && !ticket.Hidden);
        unchanged.ShouldBeTrue();
    }

    [Test]
    public async Task SetAppendAndHide_AnotherModerator_CanModifyWithoutChangingOriginalAttribution()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        var created = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "Original");
        var actor = TicketTestFixture.Moderator with { UserId = 302 };

        // Act
        await fixture.Workflow.SetAsync(actor, created.Ticket.Number, comment: "Replacement");
        var appended = await fixture.Workflow.AppendAsync(actor, created.Ticket.Number, "\nMore");
        var hidden = await fixture.Workflow.HideAsync(actor, created.Ticket.Number);

        // Assert
        appended.Ticket.Comment.ShouldBe("Replacement\nMore");
        hidden.Ticket.ModeratorUserId.ShouldBe(300UL);
        hidden.Ticket.LastModifiedByUserId.ShouldBe(302UL);
        hidden.Ticket.CreatedAtUtc!.Value.UtcDateTime.ShouldBe(
            created.Ticket.CreatedAtUtc!.Value.UtcDateTime, TimeSpan.FromMicroseconds(1));
        hidden.Ticket.Hidden.ShouldBeTrue();
        hidden.LinkedCopyRemains.ShouldBeFalse();
    }

    [Test]
    public async Task SetAsync_NoFields_DoesNotWriteOrUpdateChannel()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        var created = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "Original");

        // Act
        var result = await fixture.Workflow.SetAsync(TicketTestFixture.Moderator, created.Ticket.Number);

        // Assert
        result.NoChange.ShouldBeTrue();
        result.Ticket.Version.ShouldBe(0);
        result.Ticket.LastModifiedAtUtc.ShouldBeNull();
        fixture.Discord.Updates.ShouldBe(0);
    }

    [Test]
    public async Task SetAsync_NoteDuration_RejectsEntireChange()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.StoreAsync();

        // Act
        await Should.ThrowAsync<ArgumentException>(() => fixture.Workflow.SetAsync(
            TicketTestFixture.Moderator, 32702, duration: "1d", comment: "Must not save"));

        // Assert
        var unchanged = await fixture.Workflow.PrepareEditAsync(TicketTestFixture.Moderator, 32702);
        unchanged.Comment.ShouldBe("Stored comment");
        unchanged.Version.ShouldBe(0);
    }

    [Test]
    public async Task SetAsync_StaleModal_DoesNotOverwriteNewerComment()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        var created = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "Original");
        var opened = await fixture.Workflow.PrepareEditAsync(TicketTestFixture.Moderator, created.Ticket.Number);
        await fixture.Workflow.AppendAsync(TicketTestFixture.Moderator, created.Ticket.Number, " newer");

        // Act
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => fixture.Workflow.SetAsync(
            TicketTestFixture.Moderator, created.Ticket.Number, comment: "Stale", expectedVersion: opened.Version));

        // Assert
        var current = await fixture.Workflow.PrepareEditAsync(TicketTestFixture.Moderator, created.Ticket.Number);
        current.Comment.ShouldBe("Original newer");
        fixture.Discord.Updates.ShouldBe(1);
    }

    [Test]
    public async Task SetAsync_ConcurrentCorrections_PublishesInSaveOrder()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        var created = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "Original");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Discord.BeforeUpdate = async ticket =>
        {
            if (string.Equals(ticket.Comment, "First", StringComparison.Ordinal))
            {
                entered.SetResult();
                await release.Task;
            }
        };

        // Act
        var first = fixture.Workflow.SetAsync(TicketTestFixture.Moderator, created.Ticket.Number, comment: "First");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = fixture.Workflow.SetAsync(TicketTestFixture.Moderator, created.Ticket.Number, comment: "Second");
        release.SetResult();
        await Task.WhenAll(first, second);

        // Assert
        fixture.Discord.PublishedComments.ShouldBe(new[] { "Original", "First", "Second" });
        var current = await fixture.Workflow.PrepareEditAsync(TicketTestFixture.Moderator, created.Ticket.Number);
        current.Comment.ShouldBe("Second");
    }

    [Test]
    public async Task SetAsync_ForeignMessage_PreservesLinkAndReusesCorrection()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        await fixture.StoreAsync();
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var record = await dbContext.Tickets
            .SingleAsync();

        record.ChannelId = 200;
        record.MessageId = 900;
        await dbContext.SaveChangesAsync();

        // Act
        await fixture.Workflow.SetAsync(TicketTestFixture.Moderator, 32702, comment: "First");
        var updated = await fixture.Workflow.SetAsync(TicketTestFixture.Moderator, 32702, comment: "Second");
        var linkLookup = await fixture.Workflow.ShowMessageAsync(
            TicketTestFixture.Moderator, "https://discord.com/channels/100/200/900");
        var hidden = await fixture.Workflow.HideAsync(TicketTestFixture.Moderator, 32702);

        // Assert
        updated.Ticket.PreviousMessageId.ShouldBe(900UL);
        updated.Ticket.MessageId.ShouldBe(401UL);
        fixture.Discord.Publications.ShouldBe(1);
        fixture.Discord.Updates.ShouldBe(1);
        linkLookup.Value.ShouldBeOfType<Some<Ticket>>();
        hidden.LinkedCopyRemains.ShouldBeTrue();
    }

    [Test]
    public async Task ReadsAndModalSubmission_RevokedPermissions_AreRechecked()
    {
        // Arrange
        await using var fixture = new TicketTestFixture();
        await fixture.EnableAsync();
        var created = await fixture.Workflow.CreateAsync(TicketTestFixture.Moderator, 123, "Original");
        var noStaff = TicketTestFixture.Moderator with { CanTimeoutMembers = false };

        // Act / Assert
        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            fixture.Workflow.ListAsync(noStaff, 123, page: 2, hidden: true));
        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            fixture.Workflow.ShowAsync(noStaff, created.Ticket.Number));
        await Should.ThrowAsync<UnauthorizedAccessException>(() => fixture.Workflow.SetAsync(
            noStaff, created.Ticket.Number, comment: "Denied", expectedVersion: 0));

        fixture.Discord.DenyRead = true;
        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            fixture.Workflow.ListAsync(TicketTestFixture.Moderator, 123));
    }
}