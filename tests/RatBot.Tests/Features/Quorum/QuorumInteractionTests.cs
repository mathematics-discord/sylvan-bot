using Discord;
using Discord.Interactions;
using NSubstitute;
using RatBot.Features.Quorum;
using RatBot.Features.Quorum.Commands;
using RatBot.Features.Quorum.Persistence;
using RatBot.Tests.Integration;
using Serilog;

namespace RatBot.Tests.Features.Quorum;

[TestFixture]
public sealed class QuorumInteractionTests
{
    private const ulong GuildId = 100;
    private const ulong ChannelId = 200;
    private const ulong RoleId = 300;
    private QuorumConfigurationStore _store = null!;
    private QuorumAdminModule _admin = null!;
    private QuorumModule _quorum = null!;
    private IGuild _gld = null!;
    private ITextChannel _ch = null!;
    private IDiscordInteraction _interaction = null!;
    private readonly List<string> _responses = [];

    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
        _store = PostgresDatabaseFixture.CreateQuorumConfigurationStore();
        _gld = Substitute.For<IGuild>();
        _gld.Id.Returns(GuildId);
        _ch = CreateChannel<ITextChannel>(GuildId, ChannelId);
        _interaction = Substitute.For<IDiscordInteraction>();
        _responses.Clear();

        _interaction.ModifyOriginalResponseAsync(Arg.Any<Action<MessageProperties>>())
            .Returns(call =>
            {
                var properties = new MessageProperties();
                Action<MessageProperties> modify = call.Arg<Action<MessageProperties>>()
                                                   ?? throw new InvalidOperationException("Expected a response edit.");
                modify(properties);
                _responses.Add(properties.Content.Value);
                return Task.FromResult(Substitute.For<IUserMessage>());
            });

        var context = Substitute.For<IInteractionContext>();
        context.Guild.Returns(_gld);
        context.Channel.Returns(_ch);
        context.Interaction.Returns(_interaction);
        _admin = new QuorumAdminModule(_store);
        _quorum = new QuorumModule(_store, new LoggerConfiguration().CreateLogger());
        ((IInteractionModuleBase)_admin).SetContext(context);
        ((IInteractionModuleBase)_quorum).SetContext(context);
    }

    [Test]
    public async Task CalculateAsync_AcknowledgesBeforeMemberFetch_ThenPublishesRoundedResult()
    {
        // Arrange
        await RegisterWithRoleAsync();
        var members = new TaskCompletionSource<IReadOnlyCollection<IGuildUser>>();
        _gld.GetUsersAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>()).Returns(members.Task);

        // Act
        var calculation = _quorum.CalculateAsync();

        // The acknowledgement precedes the unfinished member fetch.
        await _interaction.Received(1).RespondAsync("Calculating quorum…", ephemeral: true);
        await _interaction.DidNotReceiveWithAnyArgs().FollowupAsync();
        calculation.IsCompleted.ShouldBeFalse();
        members.SetResult(Enumerable.Range(1, 7)
            .Select(id => CreateMember((ulong)id))
            .ToArray());
        await calculation;

        // Assert
        await _interaction.Received(1).FollowupAsync(
            "Eligible voters: 7\nProportion: 60%\nRequired quorum: 5",
            allowedMentions: Arg.Any<AllowedMentions>());
        await _interaction.Received(1).DeleteOriginalResponseAsync();
    }

    [Test]
    public async Task CalculateAsync_WhenConfigurationIncomplete_KeepsErrorEphemeral()
    {
        await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);

        await _quorum.CalculateAsync(_ch);

        _responses.ShouldBe([QuorumMessages.ConfigurationIncomplete]);
        await _interaction.Received(1).RespondAsync("Calculating quorum…", ephemeral: true);
        await _interaction.DidNotReceiveWithAnyArgs().FollowupAsync();
        await _gld.DidNotReceiveWithAnyArgs().GetUsersAsync();
    }

    [Test]
    public async Task CalculateAsync_WhenNoEligibleVoters_KeepsErrorEphemeral()
    {
        await RegisterWithRoleAsync();
        _gld.GetUsersAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>())
            .Returns(Task.FromResult<IReadOnlyCollection<IGuildUser>>([]));

        await _quorum.CalculateAsync(_ch);

        _responses.ShouldBe([QuorumMessages.NoEligibleVoters]);
        await _interaction.DidNotReceiveWithAnyArgs().FollowupAsync();
    }

    [Test]
    public async Task CalculateAsync_WhenMemberFetchFails_KeepsErrorEphemeral()
    {
        await RegisterWithRoleAsync();
        _gld.GetUsersAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>())
            .Returns(Task.FromException<IReadOnlyCollection<IGuildUser>>(new HttpRequestException("Fetch failed")));

        await _quorum.CalculateAsync(_ch);

        _responses.ShouldBe([QuorumMessages.MemberDataUnavailable]);
        await _interaction.DidNotReceiveWithAnyArgs().FollowupAsync();
    }

    [Test]
    public async Task InspectAsync_ShowsPublicConfiguration()
    {
        await RegisterWithRoleAsync(0.625m);

        await _quorum.InspectAsync(_ch);

        await _interaction.Received(1).RespondAsync("Loading quorum configuration…", ephemeral: true);
        await _interaction.Received(1).FollowupAsync(
            $"Channel: <#{ChannelId}>\nProportion: 62.5%\nVoter roles: <@&{RoleId}>\nComplete: yes",
            allowedMentions: Arg.Any<AllowedMentions>());
        await _interaction.Received(1).DeleteOriginalResponseAsync();
    }

    [Test]
    public async Task InspectAsync_WhenMissing_KeepsErrorEphemeral()
    {
        await _quorum.InspectAsync(_ch);

        _responses.ShouldBe([QuorumMessages.ConfigurationNotFound]);
        await _interaction.DidNotReceiveWithAnyArgs().FollowupAsync();
    }

    [Test]
    public async Task RegisterAsync_RegistersForumAndRespondsEphemerally()
    {
        var ch = CreateChannel<IForumChannel>(GuildId, ChannelId);

        await _admin.RegisterAsync(ch, 0.6);

        (await _store.GetAsync(GuildId, ChannelId, CancellationToken.None)).ChannelKind.ShouldBe("forum");
        _responses.ShouldBe([$"Registered quorum for <#{ChannelId}> at 60%."]);
        await _interaction.Received(1).DeferAsync(ephemeral: true);
    }

    [Test]
    public async Task RegisterAsync_RejectsCategoryAndDifferentGuildChannelsEphemerally()
    {
        await _admin.RegisterAsync(CreateChannel<ICategoryChannel>(GuildId, ChannelId), 0.6);
        await _admin.RegisterAsync(CreateChannel<ITextChannel>(GuildId + 1, ChannelId), 0.6);

        await _interaction.Received(1).RespondAsync("Choose a guild text channel or forum channel.", ephemeral: true);
        await _interaction.Received(1).RespondAsync("That channel does not belong to this guild.", ephemeral: true);
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            _store.GetAsync(GuildId, ChannelId, CancellationToken.None));
    }

    [TestCase(0)]
    [TestCase(-0.1)]
    [TestCase(1.1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public async Task RegisterAsync_InvalidProportion_ReturnsValidationError(double proportion)
    {
        await _admin.RegisterAsync(_ch, proportion);

        await _interaction.Received(1)
            .RespondAsync("Quorum proportion must be greater than 0 and at most 1.", ephemeral: true);
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            _store.GetAsync(GuildId, ChannelId, CancellationToken.None));
    }

    [Test]
    public async Task RegisterAsync_UpdatesProportionWithoutRemovingRoles()
    {
        await RegisterWithRoleAsync();

        await _admin.RegisterAsync(_ch, 0.75);

        var configuration = await _store.GetAsync(GuildId, ChannelId, CancellationToken.None);
        configuration.Proportion.ShouldBe(0.75m);
        configuration.RoleIds.ShouldContain(RoleId);
        _responses.ShouldBe([$"Updated quorum for <#{ChannelId}> at 75%."]);
    }

    [Test]
    public async Task RoleAsync_AddAndRemoveAreIdempotent()
    {
        await _store.RegisterAsync(_ch, 0.5m, CancellationToken.None);
        var role = CreateRole(_gld);

        await _admin.RoleAsync(_ch, role);
        await _admin.RoleAsync(_ch, role);
        (await _store.GetAsync(GuildId, ChannelId, CancellationToken.None)).RoleIds.ShouldBe([RoleId]);

        await _admin.RoleAsync(_ch, role, false);
        await _admin.RoleAsync(_ch, role, false);

        (await _store.GetAsync(GuildId, ChannelId, CancellationToken.None)).RoleIds.ShouldBeEmpty();
        await _interaction.Received(4).DeferAsync(ephemeral: true);
        _responses.Count.ShouldBe(4);
    }

    [Test]
    public async Task RoleAsync_RejectsRoleFromAnotherGuild()
    {
        var otherGuild = Substitute.For<IGuild>();
        otherGuild.Id.Returns(GuildId + 1);

        await _admin.RoleAsync(_ch, CreateRole(otherGuild));

        await _interaction.Received(1).RespondAsync("That role does not belong to this guild.", ephemeral: true);
        await _interaction.DidNotReceiveWithAnyArgs().DeferAsync();
    }

    [Test]
    public async Task RemoveAsync_RemovesConfigurationAndRespondsEphemerally()
    {
        await RegisterWithRoleAsync();

        await _admin.RemoveAsync(_ch);

        await Should.ThrowAsync<KeyNotFoundException>(() =>
            _store.GetAsync(GuildId, ChannelId, CancellationToken.None));
        _responses.ShouldBe([$"Removed quorum configuration for <#{ChannelId}>."]);
        await _interaction.Received(1).DeferAsync(ephemeral: true);
    }

    private async Task RegisterWithRoleAsync(decimal proportion = 0.6m)
    {
        await _store.RegisterAsync(_ch, proportion, CancellationToken.None);
        await _store.UpdateRoleAsync(GuildId, ChannelId, RoleId, true, CancellationToken.None);
    }

    private static T CreateChannel<T>(ulong guildId, ulong channelId) where T : class, IGuildChannel
    {
        var ch = Substitute.For<T>();
        ch.GuildId.Returns(guildId);
        ch.Id.Returns(channelId);
        return ch;
    }

    private static IGuildUser CreateMember(ulong id)
    {
        var member = Substitute.For<IGuildUser>();
        member.Id.Returns(id);
        member.RoleIds.Returns(new[] { RoleId });
        return member;
    }

    private static IRole CreateRole(IGuild gld)
    {
        var role = Substitute.For<IRole>();
        role.Id.Returns(RoleId);
        role.Guild.Returns(gld);
        role.Mention.Returns($"<@&{RoleId}>");
        return role;
    }
}