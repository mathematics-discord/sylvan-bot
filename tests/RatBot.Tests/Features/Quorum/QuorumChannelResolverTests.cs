using Discord;
using NSubstitute;
using RatBot.Features.Quorum.Commands;
using Shouldly;

namespace RatBot.Tests.Features.Quorum;

[TestFixture]
public sealed class QuorumChannelResolverTests
{
    [Test]
    public void ResolveChannel_AcceptsTextAndForumChannels()
    {
        IGuild gld = Substitute.For<IGuild>();
        gld.Id.Returns(100UL);
        ITextChannel text = Substitute.For<ITextChannel>();
        text.GuildId.Returns(100UL);
        IForumChannel forum = Substitute.For<IForumChannel>();
        forum.GuildId.Returns(100UL);

        QuorumChannelResolver.ResolveChannel(gld, text).Value.ShouldBeSameAs(text);
        QuorumChannelResolver.ResolveChannel(gld, forum).Value.ShouldBeSameAs(forum);
    }

    [Test]
    public void ResolveChannel_RejectsDirectMessages()
    {
        ITextChannel ch = Substitute.For<ITextChannel>();

        var result = QuorumChannelResolver.ResolveChannel(null, ch);

        result.FirstError.Description.ShouldBe("This command can only be used in a guild.");
    }

    [Test]
    public void ResolveChannel_WhenThreadParentIsUnavailable_ReturnsMeaningfulFailure()
    {
        IGuild gld = Substitute.For<IGuild>();
        gld.Id.Returns(100UL);
        IThreadChannel thread = Substitute.For<IThreadChannel>();
        thread.GuildId.Returns(100UL);

        var result = QuorumChannelResolver.ResolveChannel(gld, thread);

        result.FirstError.Description.ShouldBe("The thread's parent channel could not be resolved.");
    }
}