using System.Collections.Immutable;
using Discord;
using Discord.Interactions;
using ErrorOr;
using NSubstitute;
using RatBot.Application.Features.Quorum;
using RatBot.Domain.Features.Quorum;
using RatBot.Features.Quorum.Commands;
using Shouldly;

namespace RatBot.Discord.Tests.Features.Quorum;

[TestFixture]
public sealed class QuorumInteractionTests
{
    private const ulong ChannelId = 200;
    private const ulong GuildId = 100;
    private const ulong ParentChannelId = 400;
    private const ulong RoleId = 300;

    [Test]
    public async Task CalculateAsync_ShowsEligibleCountProportionAndRequiredQuorum()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel channel = QuorumInteractionFixture.CreateTextChannel();
        await fixture.Admin.RegisterAsync(channel, 0.6);
        await fixture.Admin.RoleAsync(channel, fixture.CreateRole());
        fixture.MemberSource.EligibleVoterCount = 7;

        fixture.ClearResponses();
        await fixture.Quorum.CalculateAsync(channel);

        await fixture.Interaction.Received(1).RespondAsync("Eligible voters: 7\nProportion: 60%\nRequired quorum: 5");
    }

    [Test]
    public async Task CalculateAsync_WhenConfigurationIncomplete_ReturnsEphemeralError()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel channel = QuorumInteractionFixture.CreateTextChannel();
        await fixture.Admin.RegisterAsync(channel, 0.5);

        fixture.ClearResponses();
        await fixture.Quorum.CalculateAsync(channel);

        await fixture.Interaction.Received(1)
            .RespondAsync(QuorumErrors.ConfigurationIncomplete.Description, ephemeral: true);
    }

    [Test]
    public async Task CalculateAsync_WhenNoEligibleVoters_ReturnsApplicationErrorEphemerally()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel channel = QuorumInteractionFixture.CreateTextChannel();
        await fixture.Admin.RegisterAsync(channel, 0.5);
        await fixture.Admin.RoleAsync(channel, fixture.CreateRole());
        fixture.MemberSource.EligibleVoterCount = 0;

        fixture.ClearResponses();
        await fixture.Quorum.CalculateAsync(channel);

        await fixture.Interaction.Received(1).RespondAsync("No eligible voters were found.", ephemeral: true);
    }

    [Test]
    public async Task InspectAsync_ShowsChannelProportionRolesAndCompleteness()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel channel = QuorumInteractionFixture.CreateTextChannel();
        IRole role = fixture.CreateRole();
        await fixture.Admin.RegisterAsync(channel, 0.625);
        await fixture.Admin.RoleAsync(channel, role);

        fixture.ClearResponses();
        await fixture.Quorum.InspectAsync(channel);

        string expected = $"Channel: <#{ChannelId}>\nProportion: 62.5%\nVoter roles: <@&{RoleId}>\nComplete: yes";
        await fixture.Interaction.Received(1).RespondAsync(expected);
    }

    [Test]
    public async Task RegisterAsync_RegistersForumScopeAndRespondsEphemeral()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        IForumChannel channel = QuorumInteractionFixture.CreateForumChannel();

        await fixture.Admin.RegisterAsync(channel, 0.6);

        fixture.Store.Configuration.ShouldNotBeNull();
        fixture.Store.Configuration.Scope.ShouldBe(new QuorumScope.ForumChannel(GuildId, ChannelId));
        await fixture.Interaction.Received(1)
            .RespondAsync($"Registered quorum for <#{ChannelId}> at 60%.", ephemeral: true);
    }

    [Test]
    public async Task RegisterAsync_RejectsCategoryAndDifferentGuildChannelsEphemerally()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ICategoryChannel category = QuorumInteractionFixture.CreateCategoryChannel();

        await fixture.Admin.RegisterAsync(category, 0.6);

        await fixture.Interaction.Received(1)
            .RespondAsync("Choose a guild text channel or forum channel.", ephemeral: true);
        fixture.Store.Configuration.ShouldBeNull();

        fixture.ClearResponses();
        ITextChannel otherGuildChannel = QuorumInteractionFixture.CreateTextChannel(GuildId + 1);

        await fixture.Admin.RegisterAsync(otherGuildChannel, 0.6);

        await fixture.Interaction.Received(1)
            .RespondAsync("That channel does not belong to this guild.", ephemeral: true);
        fixture.Store.Configuration.ShouldBeNull();
    }

    [Test]
    public async Task RegisterAsync_UpdatesTextChannelConfigurationWithoutRemovingRoles()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel channel = QuorumInteractionFixture.CreateTextChannel();
        IRole role = fixture.CreateRole();
        await fixture.Admin.RegisterAsync(channel, 0.5);
        await fixture.Admin.RoleAsync(channel, role);

        fixture.ClearResponses();
        await fixture.Admin.RegisterAsync(channel, 0.75);

        fixture.Store.Configuration!.Scope.ShouldBe(new QuorumScope.TextChannel(GuildId, ChannelId));
        fixture.Store.Configuration.Proportion.Value.ShouldBe(0.75m);
        fixture.Store.Configuration.VoterRoles.RoleIds.ShouldContain(RoleId);
        await fixture.Interaction.Received(1)
            .RespondAsync($"Updated quorum for <#{ChannelId}> at 75%.", ephemeral: true);
    }

    [Test]
    public async Task RemoveAsync_RemovesConfigurationAndRespondsEphemeral()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel channel = QuorumInteractionFixture.CreateTextChannel();
        await fixture.Admin.RegisterAsync(channel, 0.5);

        fixture.ClearResponses();
        await fixture.Admin.RemoveAsync(channel);

        fixture.Store.Configuration.ShouldBeNull();
        await fixture.Interaction.Received(1)
            .RespondAsync($"Removed quorum configuration for <#{ChannelId}>.", ephemeral: true);
    }

    [Test]
    public void ResolveScopeChannel_WhenThreadParentIsForumChannel_ReturnsForumParentScope()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        IForumChannel parent = QuorumInteractionFixture.CreateForumChannel(ParentChannelId);

        ErrorOr<QuorumScope> result = QuorumScopeResolver.ResolveScopeChannel(fixture.Guild, parent);

        result.Value.ShouldBe(new QuorumScope.ForumChannel(GuildId, ParentChannelId));
    }

    [Test]
    public void ResolveScopeChannel_WhenThreadParentIsTextChannel_ReturnsTextParentScope()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel parent = QuorumInteractionFixture.CreateTextChannel(channelId: ParentChannelId);

        ErrorOr<QuorumScope> result = QuorumScopeResolver.ResolveScopeChannel(fixture.Guild, parent);

        result.Value.ShouldBe(new QuorumScope.TextChannel(GuildId, ParentChannelId));
    }

    [Test]
    public async Task RoleAsync_AddAndRemoveAreIdempotentAndUseRoleOption()
    {
        QuorumInteractionFixture fixture = new QuorumInteractionFixture();
        ITextChannel channel = QuorumInteractionFixture.CreateTextChannel();
        IRole role = fixture.CreateRole();
        await fixture.Admin.RegisterAsync(channel, 0.5);

        fixture.ClearResponses();
        await fixture.Admin.RoleAsync(channel, role);
        await fixture.Admin.RoleAsync(channel, role);

        fixture.Store.Configuration!.VoterRoles.RoleIds.ShouldBe(ImmutableHashSet.Create(RoleId));

        await fixture.Admin.RoleAsync(channel, role, false);
        await fixture.Admin.RoleAsync(channel, role, false);

        fixture.Store.Configuration.VoterRoles.IsEmpty.ShouldBeTrue();
        await fixture.Interaction.Received(4).RespondAsync(Arg.Any<string>(), ephemeral: true);
    }

    private sealed class InMemoryConfigurationStore : IQuorumConfigurationStore
    {
        public QuorumConfiguration? Configuration { get; private set; }

        public Task<ErrorOr<QuorumConfiguration>> GetAsync(QuorumScope scope, CancellationToken ct)
        {
            return Task.FromResult<ErrorOr<QuorumConfiguration>>(
                Configuration is not null && Configuration.Scope == scope
                    ? Configuration
                    : QuorumErrors.ConfigurationNotFound
            );
        }

        public Task<ErrorOr<QuorumRegistration>> RegisterAsync(QuorumScope scope, QuorumProportion proportion,
            CancellationToken ct)
        {
            bool created = Configuration is null;
            Configuration =
                Configuration is null || Configuration.Scope != scope
                    ? QuorumConfiguration.Create(scope, proportion)
                    : Configuration.WithProportion(proportion);

            return Task.FromResult<ErrorOr<QuorumRegistration>>(new QuorumRegistration(created, Configuration));
        }

        public Task<ErrorOr<QuorumConfiguration>> SaveAsync(QuorumConfiguration configuration, CancellationToken ct)
        {
            Configuration = configuration;
            return Task.FromResult<ErrorOr<QuorumConfiguration>>(configuration);
        }

        public Task<ErrorOr<Deleted>> DeleteAsync(QuorumScope scope, CancellationToken ct)
        {
            if (Configuration is null || Configuration.Scope != scope)
                return Task.FromResult<ErrorOr<Deleted>>(QuorumErrors.ConfigurationNotFound);

            Configuration = null;
            return Task.FromResult<ErrorOr<Deleted>>(Result.Deleted);
        }
    }

    private sealed class QuorumInteractionFixture
    {
        private readonly IInteractionContext _context = Substitute.For<IInteractionContext>();

        public QuorumInteractionFixture()
        {
            Guild.Id.Returns(GuildId);
            Interaction.HasResponded.Returns(false);
            _context.Guild.Returns(Guild);
            _context.Interaction.Returns(Interaction);

            QuorumOperations operations = new QuorumOperations(Store, MemberSource);
            Admin = new QuorumAdminModule(operations);
            Quorum = new QuorumModule(operations);
            ((IInteractionModuleBase)Admin).SetContext(_context);
            ((IInteractionModuleBase)Quorum).SetContext(_context);
        }

        public QuorumAdminModule Admin { get; }
        public IGuild Guild { get; } = Substitute.For<IGuild>();
        public IDiscordInteraction Interaction { get; } = Substitute.For<IDiscordInteraction>();
        public StubMemberSource MemberSource { get; } = new StubMemberSource();
        public QuorumModule Quorum { get; }
        public InMemoryConfigurationStore Store { get; } = new InMemoryConfigurationStore();

        public static ICategoryChannel CreateCategoryChannel()
        {
            ICategoryChannel channel = Substitute.For<ICategoryChannel>();
            SetChannel(channel, GuildId);
            return channel;
        }

        public static IForumChannel CreateForumChannel(ulong channelId = ChannelId)
        {
            IForumChannel channel = Substitute.For<IForumChannel>();
            SetChannel(channel, GuildId, channelId);
            return channel;
        }

        public static ITextChannel CreateTextChannel(ulong guildId = GuildId, ulong channelId = ChannelId)
        {
            ITextChannel channel = Substitute.For<ITextChannel>();
            SetChannel(channel, guildId, channelId);
            return channel;
        }

        public IRole CreateRole()
        {
            IRole role = Substitute.For<IRole>();
            role.Id.Returns(RoleId);
            role.Mention.Returns($"<@&{RoleId}>");
            role.Guild.Returns(Guild);
            return role;
        }

        public void ClearResponses()
        {
            Interaction.ClearReceivedCalls();
        }

        private static void SetChannel(IGuildChannel channel, ulong guildId, ulong channelId = ChannelId)
        {
            channel.Id.Returns(channelId);
            channel.GuildId.Returns(guildId);
        }
    }

    private sealed class StubMemberSource : IQuorumMemberSource
    {
        public int EligibleVoterCount { get; set; } = 1;

        public Task<ErrorOr<int>> CountEligibleVotersAsync(QuorumScope scope, ImmutableHashSet<ulong> roleIds,
            CancellationToken ct)
        {
            return Task.FromResult<ErrorOr<int>>(EligibleVoterCount);
        }
    }
}