using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RatBot.Features.Tickets;
using RatBot.Infrastructure.Data;
using Serilog.Core;

namespace RatBot.Tests.Features.Tickets;

[TestFixture]
public sealed class TicketInteractionTests
{
    [Test]
    public async Task ConfigAsync_DestinationOnly_RemainsDisabledAndIsolatesGuilds()
    {
        // Arrange
        await using var fixture = new TicketFixture();
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        dbContext.TicketConfigurations.Add(new TicketConfiguration { GuildId = 999, Enabled = true, DestinationChannelId = 888 });
        await dbContext.SaveChangesAsync();

        // Act
        await fixture.Module.ConfigAsync(channel: fixture.Destination);

        // Assert
        var configurations = await dbContext.TicketConfigurations
            .AsNoTracking()
            .OrderBy(configuration => configuration.GuildId)
            .ToArrayAsync();

        configurations[0].GuildId.ShouldBe(TicketFixture.GuildId);
        configurations[0].DestinationChannelId.ShouldBe(TicketFixture.ChannelId);
        configurations[0].Enabled.ShouldBeFalse();
        configurations[1].Enabled.ShouldBeTrue();
        configurations[1].DestinationChannelId.ShouldBe(888UL);
        await fixture.Interaction.Received(1).DeferAsync(ephemeral: true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConfigAsync_MissingOrForeignDestination_RejectsEnabling(bool foreignChannel)
    {
        // Arrange
        await using var fixture = new TicketFixture();
        fixture.Destination.GuildId.Returns(999UL);

        // Act
        await fixture.Module.ConfigAsync(enabled: true, channel: foreignChannel ? fixture.Destination : null);

        // Assert
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var configurations = await dbContext.TicketConfigurations
            .ToArrayAsync();

        configurations.ShouldBeEmpty();
        fixture.ResponseText.ShouldContain("before enabling");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CheckPreconditionsAsync_Config_RequiresAdministrator(bool administrator)
    {
        // Arrange
        await using var fixture = new TicketFixture();
        fixture.Author.GuildPermissions.Returns(new GuildPermissions(administrator: administrator, moderateMembers: true));
        await using var client = new DiscordSocketClient();
        using var interactions = new InteractionService(client);
        var module = await interactions.AddModuleAsync<TicketModule>(fixture.Services);
        var command = module.SlashCommands
            .Single(command => string.Equals(command.Name, "config", StringComparison.Ordinal));

        // Act
        var result = await command.CheckPreconditionsAsync(fixture.Context, fixture.Services);

        // Assert
        result.IsSuccess.ShouldBe(administrator);
    }

    [TestCase(GuildPermission.Administrator)]
    [TestCase(GuildPermission.ModerateMembers)]
    [TestCase(GuildPermission.MuteMembers)]
    public async Task NoteAsync_EachQualifyingPermission_PersistsBeforePublishing(GuildPermission permission)
    {
        // Arrange
        await using var fixture = new TicketFixture();
        await fixture.EnableAsync();
        fixture.Author.GuildPermissions.Returns(new GuildPermissions((ulong)permission));
        const string text = "  Preserved note\n@everyone <@123>  ";
        var before = DateTimeOffset.UtcNow;

        // Act
        await fixture.Module.NoteAsync("<@!123>", text);

        // Assert
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var note = await dbContext.TicketNotes
            .SingleAsync();

        note.GuildId.ShouldBe(TicketFixture.GuildId);
        note.SubjectUserId.ShouldBe(123UL);
        note.AuthorUserId.ShouldBe(TicketFixture.AuthorId);
        note.Text.ShouldBe(text);
        note.CreatedAtUtc.ShouldBeInRange(before, DateTimeOffset.UtcNow);
        note.CreatedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        note.PublishedChannelId.ShouldBe(TicketFixture.ChannelId);
        note.PublishedMessageId.ShouldBe(TicketFixture.MessageId);
        fixture.SavedBeforePost.ShouldBeTrue();
        fixture.PublishedEmbed.ShouldNotBeNull().Description.ShouldBe(text);
        fixture.PublishedEmbed.Timestamp.ShouldBe(note.CreatedAtUtc);
        fixture.PublishedEmbed.Fields[0].Value.ShouldContain("123");
        fixture.PublishedEmbed.Fields[1].Value.ShouldContain(TicketFixture.AuthorId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fixture.PostCount.ShouldBe(1);
        fixture.PublishedMentions.ShouldNotBeNull().AllowedTypes.ShouldBe(AllowedMentions.None.AllowedTypes);
        fixture.PublicationOptions.ShouldNotBeNull().RetryMode.ShouldBe(RetryMode.AlwaysFail);
        fixture.ResponseText.ShouldContain("T-0001");
        fixture.ResponseText.ShouldContain($"https://discord.com/channels/{TicketFixture.GuildId}/{TicketFixture.ChannelId}/{TicketFixture.MessageId}");
        await fixture.Guild.DidNotReceive().GetUserAsync(123, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>());
        await fixture.Interaction.Received(1).DeferAsync(ephemeral: true);
    }

    [TestCase("permission")]
    [TestCase("disabled")]
    [TestCase("other-guild-config")]
    [TestCase("unavailable")]
    [TestCase("foreign-channel")]
    [TestCase("invoker-view")]
    [TestCase("bot-view")]
    [TestCase("bot-send")]
    [TestCase("bot-embed")]
    public async Task NoteAsync_FailedPreconditions_DoesNotPersistOrPublish(string failure)
    {
        // Arrange
        await using var fixture = new TicketFixture();
        await fixture.EnableAsync(guildId: string.Equals(failure, "other-guild-config", StringComparison.Ordinal) ? 999UL : TicketFixture.GuildId);

        switch (failure)
        {
            case "permission":
                fixture.Author.GuildPermissions.Returns(GuildPermissions.None);
                break;
            case "disabled":
                await fixture.Module.ConfigAsync(enabled: false);
                break;
            case "unavailable":
                fixture.Guild.GetTextChannelAsync(TicketFixture.ChannelId).Returns((ITextChannel?)null);
                break;
            case "foreign-channel":
                fixture.Destination.GuildId.Returns(999UL);
                break;
            case "invoker-view":
                fixture.Author.GetPermissions(fixture.Destination).Returns(ChannelPermissions.None);
                break;
            case "bot-view":
                fixture.Bot.GetPermissions(fixture.Destination).Returns(new ChannelPermissions(sendMessages: true, embedLinks: true));
                break;
            case "bot-send":
                fixture.Bot.GetPermissions(fixture.Destination).Returns(new ChannelPermissions(viewChannel: true, embedLinks: true));
                break;
            case "bot-embed":
                fixture.Bot.GetPermissions(fixture.Destination).Returns(new ChannelPermissions(viewChannel: true, sendMessages: true));
                break;
        }

        // Act
        await fixture.Module.NoteAsync("123", "A note");

        // Assert
        await fixture.AssertNoNoteOrPostAsync();
    }

    [TestCase("0", "text")]
    [TestCase("<@&123>", "text")]
    [TestCase("not a user", "text")]
    [TestCase("123", " \n\t")]
    [TestCase("123", "")]
    public async Task NoteAsync_InvalidInput_DoesNotPersistOrPublish(string user, string text)
    {
        // Arrange
        await using var fixture = new TicketFixture();
        await fixture.EnableAsync();

        // Act
        await fixture.Module.NoteAsync(user, text);

        // Assert
        await fixture.AssertNoNoteOrPostAsync();
    }

    [TestCase(2000, true)]
    [TestCase(2001, false)]
    public async Task NoteAsync_TextLength_EnforcesLimit(int length, bool accepted)
    {
        // Arrange
        await using var fixture = new TicketFixture();
        await fixture.EnableAsync();

        // Act
        await fixture.Module.NoteAsync("123", new string('x', length));

        // Assert
        fixture.PostCount.ShouldBe(accepted ? 1 : 0);
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var notes = await dbContext.TicketNotes
            .ToArrayAsync();

        notes.Length.ShouldBe(accepted ? 1 : 0);
    }

    [Test]
    public async Task NoteAsync_InitialSaveFails_DoesNotPublish()
    {
        // Arrange
        await using var fixture = new TicketFixture(failSaveState: EntityState.Added);
        await fixture.EnableAsync();

        // Act
        await fixture.Module.NoteAsync("123", "A note");

        // Assert
        await fixture.AssertNoNoteOrPostAsync();
        fixture.ResponseText.ShouldContain("No channel post was attempted");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NoteAsync_PublicationOrReferenceSaveFails_RetainsNoteAndReportsOutcome(bool referenceSaveFails)
    {
        // Arrange
        await using var fixture = new TicketFixture(failSaveState: referenceSaveFails ? EntityState.Modified : null);
        await fixture.EnableAsync();
        fixture.PublicationFails = !referenceSaveFails;

        // Act
        await fixture.Module.NoteAsync("123", "A note");

        // Assert
        await using var dbContext = await fixture.Factory.CreateDbContextAsync();
        var note = await dbContext.TicketNotes
            .SingleAsync();

        note.Text.ShouldBe("A note");
        note.PublishedChannelId.ShouldBeNull();
        note.PublishedMessageId.ShouldBeNull();
        fixture.PostCount.ShouldBe(1);
        fixture.ResponseText.ShouldContain("T-0001 was saved");
        fixture.ResponseText.ShouldContain(referenceSaveFails ? "Saving its channel/message reference failed" : "publication failed");

        if (referenceSaveFails)
            fixture.ResponseText.ShouldContain($"/{TicketFixture.MessageId}");
    }

    private sealed class TicketFixture : IAsyncDisposable
    {
        public const ulong GuildId = 100;
        public const ulong ChannelId = 200;
        public const ulong AuthorId = 300;
        public const ulong MessageId = 400;

        public TicketFixture(EntityState? failSaveState = null)
        {
            var databaseName = Guid.NewGuid().ToString();
            Services = new ServiceCollection()
                .AddSingleton<Serilog.ILogger>(Logger.None)
                .AddDbContextFactory<BotDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    .AddInterceptors(new FailingNoteSaveInterceptor(failSaveState)))
                .BuildServiceProvider();

            Factory = Services.GetRequiredService<IDbContextFactory<BotDbContext>>();
            ConfigureContext();
            ConfigurePublication();
            Module = new TicketModule(Factory, Logger.None);
            ((IInteractionModuleBase)Module).SetContext(Context);
        }

        public ServiceProvider Services { get; }
        public IDbContextFactory<BotDbContext> Factory { get; }
        public TicketModule Module { get; }
        public IGuild Guild { get; } = Substitute.For<IGuild>();
        public IGuildUser Author { get; } = Substitute.For<IGuildUser>();
        public IGuildUser Bot { get; } = Substitute.For<IGuildUser>();
        public ITextChannel Destination { get; } = Substitute.For<ITextChannel>();
        public IInteractionContext Context { get; } = Substitute.For<IInteractionContext>();
        public IDiscordInteraction Interaction { get; } = Substitute.For<IDiscordInteraction>();
        public string ResponseText { get; private set; } = string.Empty;
        public bool PublicationFails { get; set; }
        public bool SavedBeforePost { get; private set; }
        public int PostCount { get; private set; }
        public Embed? PublishedEmbed { get; private set; }
        public AllowedMentions? PublishedMentions { get; private set; }
        public RequestOptions? PublicationOptions { get; private set; }

        public async Task EnableAsync(ulong guildId = GuildId)
        {
            await using var dbContext = await Factory.CreateDbContextAsync();
            dbContext.TicketConfigurations.Add(new TicketConfiguration
            {
                GuildId = guildId,
                Enabled = true,
                DestinationChannelId = ChannelId,
            });

            await dbContext.SaveChangesAsync();
        }

        public async Task AssertNoNoteOrPostAsync()
        {
            await using var dbContext = await Factory.CreateDbContextAsync();
            var notes = await dbContext.TicketNotes
                .ToArrayAsync();

            notes.ShouldBeEmpty();
            PostCount.ShouldBe(0);
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();

        private void ConfigureContext()
        {
            Guild.Id.Returns(GuildId);
            Author.Id.Returns(AuthorId);
            Author.GuildPermissions.Returns(new GuildPermissions(muteMembers: true));
            Author.GetPermissions(Destination).Returns(new ChannelPermissions(viewChannel: true));
            Bot.GetPermissions(Destination).Returns(new ChannelPermissions(viewChannel: true, sendMessages: true, embedLinks: true));
            Destination.Id.Returns(ChannelId);
            Destination.GuildId.Returns(GuildId);
            Destination.ChannelType.Returns(ChannelType.Text);
            Guild.GetTextChannelAsync(ChannelId).Returns(Destination);
            Guild.GetCurrentUserAsync().Returns(Bot);
            Context.Guild.Returns(Guild);
            Context.User.Returns(Author);
            Context.Channel.Returns(Destination);
            Context.Interaction.Returns(Interaction);
            Interaction.ModifyOriginalResponseAsync(Arg.Any<Action<MessageProperties>>(), Arg.Any<RequestOptions>())
                .Returns(call =>
                {
                    var properties = new MessageProperties();
                    call.Arg<Action<MessageProperties>>().ShouldNotBeNull()(properties);
                    ResponseText = properties.Content.Value;
                    return Task.FromResult(Substitute.For<IUserMessage>());
                });
        }

        private void ConfigurePublication()
        {
            Destination.SendMessageAsync(embed: Arg.Any<Embed>(), options: Arg.Any<RequestOptions>(), allowedMentions: Arg.Any<AllowedMentions>())
                .Returns(async call =>
                {
                    PostCount++;
                    PublishedEmbed = call.Arg<Embed>();
                    PublishedMentions = call.Arg<AllowedMentions>();
                    PublicationOptions = call.Arg<RequestOptions>();
                    await using var dbContext = await Factory.CreateDbContextAsync();
                    var note = await dbContext.TicketNotes
                        .AsNoTracking()
                        .SingleAsync();

                    SavedBeforePost = note.PublishedMessageId is null && note.PublishedChannelId is null;

                    if (PublicationFails)
                        throw new InvalidOperationException("Publication failed");

                    var msg = Substitute.For<IUserMessage>();
                    msg.Id.Returns(MessageId);
                    return msg;
                });
        }
    }

    private sealed class FailingNoteSaveInterceptor(EntityState? failSaveState) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var shouldFail = eventData.Context!.ChangeTracker.Entries<TicketNote>()
                .Any(entry => entry.State == failSaveState);

            if (shouldFail)
                throw new DbUpdateException("Simulated save failure");

            return ValueTask.FromResult(result);
        }
    }
}
