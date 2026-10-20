namespace RatBot.Tests.Features.Quorum;

using Discord;
using NSubstitute;
using RatBot.Application.Common;
using RatBot.Features.Quorum.Commands;

[TestFixture]
public sealed class QuorumChannelResolverTests
{
    [Test]
    public void ResolveChannel_AcceptsTextAndForumChannels()
    {
        var guild = Substitute.For<IGuild>();
        guild.Id.Returns(100UL);
        var text = Substitute.For<ITextChannel>();
        text.GuildId.Returns(100UL);
        var forum = Substitute.For<IForumChannel>();
        forum.GuildId.Returns(100UL);

        QuorumChannelResolver.ResolveChannel(guild, text).ShouldBeSameAs(text);
        QuorumChannelResolver.ResolveChannel(guild, forum).ShouldBeSameAs(forum);
    }

    [Test]
    public void ResolveChannel_RejectsDirectMessages()
    {
        var channel = Substitute.For<ITextChannel>();

        var exception = Should.Throw<BotCommandException>(() => QuorumChannelResolver.ResolveChannel(null, channel));

        exception.Message.ShouldBe("This command can only be used in a guild.");
    }

    [Test]
    public void ResolveChannel_WhenThreadParentIsUnavailable_ReturnsMeaningfulFailure()
    {
        var guild = Substitute.For<IGuild>();
        guild.Id.Returns(100UL);
        var thread = Substitute.For<IThreadChannel>();
        thread.GuildId.Returns(100UL);

        var exception = Should.Throw<BotCommandException>(() => QuorumChannelResolver.ResolveChannel(guild, thread));

        exception.Message.ShouldBe("The thread's parent channel could not be resolved.");
    }
}