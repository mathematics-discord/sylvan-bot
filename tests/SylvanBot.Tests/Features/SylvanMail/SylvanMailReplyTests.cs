namespace SylvanBot.Tests.Features.SylvanMail;

using Shouldly;
using SylvanBot.Application.Features.SylvanMail;

[TestFixture]
public sealed class SylvanMailReplyTests
{
    [TestCase("<@42> hello", false, false)]
    [TestCase("<@!42> hello", false, false)]
    [TestCase("<@42> hello", true, true)]
    [TestCase("<@!42> hello", true, false)]
    public void Parse_ExplicitBodyMention_UsesAnonymousMode(string text, bool reply, bool ping)
    {
        // Act
        var draft = SylvanMailReply.Parse(text, 42, reply, ping);

        // Assert
        draft.ShouldNotBeNull();
        draft.Anonymous.ShouldBeTrue();
        draft.Content.ShouldBe("hello");
    }

    [Test]
    public void Parse_PingedReplyToSylvan_UsesSignedMode()
    {
        // Act
        var draft = SylvanMailReply.Parse("hello <@99>", 42, replyToBot: true, replyPingEnabled: true);

        // Assert
        draft.ShouldNotBeNull();
        draft.Anonymous.ShouldBeFalse();
        draft.Content.ShouldBe("hello <@99>");
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void Parse_MissingReplyTrigger_LeavesDiscussionUnsent(bool reply, bool ping)
    {
        // Act
        var draft = SylvanMailReply.Parse("hello <@99>", 42, reply, ping);

        // Assert
        draft.ShouldBeNull();
    }

    [Test]
    public void Parse_EditedBodyMention_ReevaluatesSignedMode()
    {
        // Arrange
        var original = SylvanMailReply.Parse("<@42> hello", 42, replyToBot: true, replyPingEnabled: true);

        // Act
        var edited = SylvanMailReply.Parse("hello", 42, replyToBot: true, replyPingEnabled: true);

        // Assert
        original.ShouldNotBeNull().Anonymous.ShouldBeTrue();
        edited.ShouldNotBeNull().Anonymous.ShouldBeFalse();
    }

    [Test]
    public void Parse_AttachmentOnlyMention_PreservesEmptyBodyForAttachmentValidation()
    {
        // Act
        var draft = SylvanMailReply.Parse("<@42>", 42, replyToBot: false, replyPingEnabled: false);

        // Assert
        draft.ShouldNotBeNull();
        draft.Anonymous.ShouldBeTrue();
        draft.Content.ShouldBeEmpty();
    }
}
