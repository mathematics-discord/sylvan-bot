using Discord;
using NSubstitute;
using RatBot.Features.Quorum.Commands;

namespace RatBot.Tests.Features.Quorum;

[TestFixture]
public sealed class QuorumChannelResolverTests
{
    [Test]
    public void ResolveChannel_AcceptsTextAndForumChannels()
    {
        var gld = Substitute.For<IGuild>();
        gld.Id.Returns(100UL);
        var text = Substitute.For<ITextChannel>();
        text.GuildId.Returns(100UL);
        var forum = Substitute.For<IForumChannel>();
        forum.GuildId.Returns(100UL);

        QuorumChannelResolver.ResolveChannel(gld, text).ShouldBeSameAs(text);
        QuorumChannelResolver.ResolveChannel(gld, forum).ShouldBeSameAs(forum);
    }

    [Test]
    public void ResolveChannel_RejectsDirectMessages()
    {
        var ch = Substitute.For<ITextChannel>();

        var exception = Should.Throw<ArgumentException>(() => QuorumChannelResolver.ResolveChannel(null, ch));

        exception.Message.ShouldContain("This command can only be used in a guild.");
    }

    [Test]
    public void ResolveChannel_ThreadParentUnavailable_ThrowsArgumentException()
    {
        var gld = Substitute.For<IGuild>();
        gld.Id.Returns(100UL);
        var thread = Substitute.For<IThreadChannel>();
        thread.GuildId.Returns(100UL);

        var exception = Should.Throw<ArgumentException>(() => QuorumChannelResolver.ResolveChannel(gld, thread));

        exception.Message.ShouldContain("The thread's parent channel could not be resolved.");
    }
}