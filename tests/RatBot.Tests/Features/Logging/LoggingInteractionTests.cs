using Discord.Interactions;
using Discord.WebSocket;
using Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RatBot.Features.Logging;
using RatBot.Infrastructure.Data;
using Shouldly;

namespace RatBot.Tests.Features.Logging;

[TestFixture]
public sealed class LoggingInteractionTests
{
    private const ulong ChannelId = 200;
    private const ulong DeleteLogChannelId = 300;
    private const ulong EditLogChannelId = 400;
    private const ulong GuildId = 100;

    [Test]
    public async Task ExcludeAsync_ChannelOmitted_ExcludesCurrentChannel()
    {
        // Arrange
        await using var services = CreateServices(nameof(ExcludeAsync_ChannelOmitted_ExcludesCurrentChannel));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        var before = DateTimeOffset.UtcNow;

        // Act
        await fixture.Module.ExcludeAsync();

        // Assert
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var exclusion = await dbContext.LoggingExcludedChannels
            .SingleAsync();

        exclusion.GuildId.ShouldBe(GuildId);
        exclusion.ChannelId.ShouldBe(ChannelId);
        exclusion.ExcludedAtUtc.ShouldBeInRange(before, DateTimeOffset.UtcNow);
        fixture.ResponseText.ShouldBe($"Logging is now excluded in <#{ChannelId}>.");
        await fixture.Interaction.Received(1).DeferAsync(ephemeral: true);
    }

    [Test]
    public async Task ExcludeAsync_AlreadyExcluded_PreservesOriginalTimestamp()
    {
        // Arrange
        await using var services = CreateServices(nameof(ExcludeAsync_AlreadyExcluded_PreservesOriginalTimestamp));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        var excludedAt = DateTimeOffset.UtcNow.AddDays(-7);
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        dbContext.LoggingExcludedChannels.Add(new LoggingExcludedChannel
        {
            GuildId = GuildId,
            ChannelId = ChannelId,
            ExcludedAtUtc = excludedAt,
        });

        await dbContext.SaveChangesAsync();

        // Act
        await fixture.Module.ExcludeAsync();

        // Assert
        var exclusions = await dbContext.LoggingExcludedChannels
            .AsNoTracking()
            .ToArrayAsync();

        exclusions.ShouldHaveSingleItem().ExcludedAtUtc.ShouldBe(excludedAt);
        fixture.ResponseText.ShouldBe($"<#{ChannelId}> is already excluded from logging.");
    }

    [Test]
    public async Task IncludeAsync_ChannelExcluded_RemovesExclusion()
    {
        // Arrange
        await using var services = CreateServices(nameof(IncludeAsync_ChannelExcluded_RemovesExclusion));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        await fixture.Module.ExcludeAsync();

        // Act
        await fixture.Module.IncludeAsync();

        // Assert
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var exclusionCount = await dbContext.LoggingExcludedChannels
            .CountAsync();

        exclusionCount.ShouldBe(0);
        fixture.ResponseText.ShouldBe($"Logging is enabled again in <#{ChannelId}>.");
    }

    [Test]
    public async Task IncludeAsync_ExclusionBelongsToAnotherGuild_LeavesItUnchanged()
    {
        // Arrange
        await using var services = CreateServices(nameof(IncludeAsync_ExclusionBelongsToAnotherGuild_LeavesItUnchanged));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        var otherGuild = new LoggingInteractionFixture(contextFactory, guildId: GuildId + 1);
        await otherGuild.Module.ExcludeAsync();

        // Act
        await fixture.Module.IncludeAsync();

        // Assert
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var exclusion = await dbContext.LoggingExcludedChannels
            .SingleAsync();

        exclusion.GuildId.ShouldBe(GuildId + 1);
        exclusion.ChannelId.ShouldBe(ChannelId);
        fixture.ResponseText.ShouldBe($"<#{ChannelId}> was not excluded from logging.");
    }

    [Test]
    public async Task ExcludeAsync_OtherGuildHasExclusion_CreatesCurrentGuildExclusion()
    {
        // Arrange
        await using var services = CreateServices(nameof(ExcludeAsync_OtherGuildHasExclusion_CreatesCurrentGuildExclusion));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        var otherGuild = new LoggingInteractionFixture(contextFactory, guildId: GuildId + 1);
        await otherGuild.Module.ExcludeAsync();

        // Act
        await fixture.Module.ExcludeAsync();

        // Assert
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var guildIds = await dbContext.LoggingExcludedChannels
            .OrderBy(exclusion => exclusion.GuildId)
            .Select(exclusion => exclusion.GuildId)
            .ToArrayAsync();

        guildIds.ShouldBe([GuildId, GuildId + 1]);
        fixture.ResponseText.ShouldBe($"Logging is now excluded in <#{ChannelId}>.");
    }

    [Test]
    public async Task ExclusionsAsync_LargeGuildList_SplitsCompleteLinesAndKeepsGuildIsolation()
    {
        // Arrange
        await using var services = CreateServices(nameof(ExclusionsAsync_LargeGuildList_SplitsCompleteLinesAndKeepsGuildIsolation));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        var otherGuild = new LoggingInteractionFixture(contextFactory, guildId: GuildId + 1);
        otherGuild.Channel.Id.Returns(999UL);
        await otherGuild.Module.ExcludeAsync();

        var channelIds = Enumerable.Range(0, 100)
            .Select(index => 1234567890123456789UL + (ulong)index)
            .ToArray();

        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var exclusions = channelIds
            .Select(channelId => new LoggingExcludedChannel
            {
                GuildId = GuildId,
                ChannelId = channelId,
                ExcludedAtUtc = DateTimeOffset.UtcNow,
            });

        dbContext.LoggingExcludedChannels.AddRange(exclusions);
        await dbContext.SaveChangesAsync();
        var followups = new List<string>();
        fixture.Interaction.FollowupAsync(text: Arg.Any<string>(), ephemeral: true)
            .Returns(call =>
            {
                followups.Add(call.ArgAt<string>(0));
                return Task.FromResult(Substitute.For<IUserMessage>());
            });

        // Act
        await fixture.Module.ExclusionsAsync();

        // Assert
        var responses = new List<string> { fixture.ResponseText };
        responses.AddRange(followups);
        responses.Count.ShouldBe(2);

        foreach (var response in responses)
            response.Length.ShouldBeLessThanOrEqualTo(2000);

        var actualLines = responses
            .SelectMany(response => response.Split('\n').Skip(1))
            .ToArray();

        var expectedLines = channelIds
            .Select(channelId => $"- <#{channelId}>");

        actualLines.ShouldBe(expectedLines);
    }

    [Test]
    public async Task ConfigAsync_PartialUpdate_PreservesOmittedAndLegacyValues()
    {
        // Arrange
        await using var services = CreateServices(nameof(ConfigAsync_PartialUpdate_PreservesOmittedAndLegacyValues));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        await fixture.Module.ConfigAsync(enabled: true, deleteLogChannel: fixture.DeleteLogChannel);
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var original = await dbContext.LoggingConfigurations
            .SingleAsync();

        original.LegacyEvidenceRetentionPeriod.ShouldBeGreaterThan(TimeSpan.Zero);
        original.LegacyEvidenceRetentionPeriod = TimeSpan.FromSeconds(600);
        await dbContext.SaveChangesAsync();

        // Act
        await fixture.Module.ConfigAsync(editLogChannel: fixture.EditLogChannel);

        // Assert
        var configuration = await dbContext.LoggingConfigurations
            .AsNoTracking()
            .SingleAsync();

        configuration.Enabled.ShouldBeTrue();
        configuration.DeleteLogChannelId.ShouldBe(DeleteLogChannelId);
        configuration.EditLogChannelId.ShouldBe(EditLogChannelId);
        configuration.LegacyEvidenceRetentionPeriod.ShouldBe(TimeSpan.FromSeconds(600));
        fixture.ResponseText.ShouldBe(
            "Logging configuration:\n"
            + "- Enabled: True\n"
            + $"- Delete log channel: <#{DeleteLogChannelId}>\n"
            + $"- Edit log channel: <#{EditLogChannelId}>");
    }

    [Test]
    public async Task ConfigAsync_EnablingWithoutEitherChannel_DoesNotPersistInvalidSettings()
    {
        // Arrange
        await using var services = CreateServices(nameof(ConfigAsync_EnablingWithoutEitherChannel_DoesNotPersistInvalidSettings));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);

        // Act
        await fixture.Module.ConfigAsync(enabled: true);

        // Assert
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var configurationCount = await dbContext.LoggingConfigurations
            .CountAsync();

        configurationCount.ShouldBe(0);
        fixture.ResponseText.ShouldBe("Enable logging only after setting a delete or edit log channel.");
    }

    [Test]
    public async Task ConfigAsync_NoArguments_DoesNotCreateSettings()
    {
        // Arrange
        await using var services = CreateServices(nameof(ConfigAsync_NoArguments_DoesNotCreateSettings));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);

        // Act
        await fixture.Module.ConfigAsync();

        // Assert
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var configurationCount = await dbContext.LoggingConfigurations
            .CountAsync();

        configurationCount.ShouldBe(0);
        fixture.ResponseText.ShouldBe(
            "Logging configuration:\n- Enabled: False\n- Delete log channel: unset\n- Edit log channel: unset");
    }

    [Test]
    public async Task ConfigAsync_TwoGuildsConfigured_OnlyUpdatesCurrentGuild()
    {
        // Arrange
        await using var services = CreateServices(nameof(ConfigAsync_TwoGuildsConfigured_OnlyUpdatesCurrentGuild));
        var contextFactory = services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        var fixture = new LoggingInteractionFixture(contextFactory);
        var otherGuild = new LoggingInteractionFixture(contextFactory, guildId: GuildId + 1);
        await fixture.Module.ConfigAsync(enabled: true, deleteLogChannel: fixture.DeleteLogChannel);
        await otherGuild.Module.ConfigAsync(enabled: true, editLogChannel: otherGuild.EditLogChannel);

        // Act
        await fixture.Module.ConfigAsync(enabled: false);
        await otherGuild.Module.ConfigAsync();

        // Assert
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var configurations = await dbContext.LoggingConfigurations
            .OrderBy(configuration => configuration.GuildId)
            .ToArrayAsync();

        configurations.Length.ShouldBe(2);
        configurations[0].Enabled.ShouldBeFalse();
        configurations[0].DeleteLogChannelId.ShouldBe(DeleteLogChannelId);
        configurations[1].Enabled.ShouldBeTrue();
        configurations[1].DeleteLogChannelId.ShouldBeNull();
        configurations[1].EditLogChannelId.ShouldBe(EditLogChannelId);
        otherGuild.ResponseText.ShouldBe(
            "Logging configuration:\n- Enabled: True\n- Delete log channel: unset\n"
            + $"- Edit log channel: <#{EditLogChannelId}>");
    }

    [Test]
    public async Task BuildConfigCommand_CurrentContract_HasThreeOptionalSettings()
    {
        // Arrange
        await using var services = CreateServices(nameof(BuildConfigCommand_CurrentContract_HasThreeOptionalSettings));
        await using var discordClient = new DiscordSocketClient();
        using var interactionService = new InteractionService(discordClient);

        // Act
        var module = await interactionService.AddModuleAsync<LoggingModule>(services);
        var command = module.SlashCommands
            .Single(command => string.Equals(command.Name, "config", StringComparison.Ordinal));

        // Assert
        var names = command.Parameters
            .Select(parameter => parameter.Name);

        var required = command.Parameters
            .Select(parameter => parameter.IsRequired);

        names.ShouldBe(["enabled", "delete-log-channel", "edit-log-channel"]);
        required.ShouldBe([false, false, false]);
    }

    [TestCase("config")]
    [TestCase("exclude")]
    [TestCase("include")]
    [TestCase("exclusions")]
    public async Task CheckPreconditionsAsync_NonAdministrator_RejectsLoggingCommand(string commandName)
    {
        // Arrange
        await using var services = CreateServices(nameof(CheckPreconditionsAsync_NonAdministrator_RejectsLoggingCommand));
        var fixture = new LoggingInteractionFixture(services.GetRequiredService<IDbContextFactory<BotDbContext>>());
        fixture.User.GuildPermissions.Returns(new GuildPermissions(administrator: false));
        await using var discordClient = new DiscordSocketClient();
        using var interactionService = new InteractionService(discordClient);
        var module = await interactionService.AddModuleAsync<LoggingModule>(services);
        var command = module.SlashCommands
            .Single(command => string.Equals(command.Name, commandName, StringComparison.Ordinal));

        // Act
        var result = await command.CheckPreconditionsAsync(fixture.Context, services);

        // Assert
        result.IsSuccess.ShouldBeFalse();
    }

    [TestCase("config")]
    [TestCase("exclude")]
    [TestCase("include")]
    [TestCase("exclusions")]
    public async Task CheckPreconditionsAsync_Administrator_AllowsLoggingCommand(string commandName)
    {
        // Arrange
        await using var services = CreateServices(nameof(CheckPreconditionsAsync_Administrator_AllowsLoggingCommand));
        var fixture = new LoggingInteractionFixture(services.GetRequiredService<IDbContextFactory<BotDbContext>>());
        await using var discordClient = new DiscordSocketClient();
        using var interactionService = new InteractionService(discordClient);
        var module = await interactionService.AddModuleAsync<LoggingModule>(services);
        var command = module.SlashCommands
            .Single(command => string.Equals(command.Name, commandName, StringComparison.Ordinal));

        // Act
        var result = await command.CheckPreconditionsAsync(fixture.Context, services);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    private static ServiceProvider CreateServices(string name)
    {
        return new ServiceCollection()
            .AddDbContextFactory<BotDbContext>(options => options.UseInMemoryDatabase(name))
            .BuildServiceProvider();
    }

    private sealed class LoggingInteractionFixture
    {
        public LoggingInteractionFixture(IDbContextFactory<BotDbContext> contextFactory, ulong guildId = GuildId)
        {
            Guild.Id.Returns(guildId);
            User.GuildPermissions.Returns(new GuildPermissions(administrator: true));
            Channel.Id.Returns(ChannelId);
            Channel.GuildId.Returns(guildId);
            DeleteLogChannel.Id.Returns(DeleteLogChannelId);
            DeleteLogChannel.GuildId.Returns(guildId);
            EditLogChannel.Id.Returns(EditLogChannelId);
            EditLogChannel.GuildId.Returns(guildId);
            Context.Guild.Returns(Guild);
            Context.Channel.Returns(Channel);
            Context.User.Returns(User);
            Context.Interaction.Returns(Interaction);
            Interaction.ModifyOriginalResponseAsync(Arg.Any<Action<MessageProperties>>(), Arg.Any<RequestOptions>())
                .Returns(call =>
                {
                    var properties = new MessageProperties();
                    var update = call.Arg<Action<MessageProperties>>().ShouldNotBeNull();
                    update(properties);
                    ResponseText = properties.Content.Value;
                    return Task.FromResult(Substitute.For<IUserMessage>());
                });

            Module = new LoggingModule(contextFactory);
            ((IInteractionModuleBase)Module).SetContext(Context);
        }

        public ITextChannel Channel { get; } = Substitute.For<ITextChannel>();
        public ITextChannel DeleteLogChannel { get; } = Substitute.For<ITextChannel>();
        public ITextChannel EditLogChannel { get; } = Substitute.For<ITextChannel>();
        public IGuild Guild { get; } = Substitute.For<IGuild>();
        public IDiscordInteraction Interaction { get; } = Substitute.For<IDiscordInteraction>();
        public IInteractionContext Context { get; } = Substitute.For<IInteractionContext>();
        public LoggingModule Module { get; }
        public IGuildUser User { get; } = Substitute.For<IGuildUser>();
        public string ResponseText { get; private set; } = string.Empty;
    }
}
