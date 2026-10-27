using SylvanBot.Application.Features.SylvanMail;

namespace SylvanBot.Tests.Features.SylvanMail;

[TestFixture]
public sealed class SylvanMailReplyTests
{
    [TestCase("<@42> hello", arg2: false, arg3: false)]
    [TestCase("<@!42> hello", arg2: false, arg3: false)]
    [TestCase("<@42> hello", arg2: true, arg3: true)]
    [TestCase("<@!42> hello", arg2: true, arg3: false)]
    public void Parse_ExplicitBodyMention_UsesAnonymousMode(string text, bool reply, bool ping)
    {
        // Act
        var draft = SylvanMailReply.Parse(content: text, botId: 42, replyToBot: reply, replyPingEnabled: ping);

        // Assert
        draft.ShouldNotBeNull();
        draft.Anonymous.ShouldBeTrue();
        draft.Content.ShouldBe("hello");
    }

    [Test]
    public void Parse_PingedReplyToSylvan_UsesSignedMode()
    {
        // Act
        var draft = SylvanMailReply.Parse("hello <@99>", botId: 42, replyToBot: true, replyPingEnabled: true);

        // Assert
        draft.ShouldNotBeNull();
        draft.Anonymous.ShouldBeFalse();
        draft.Content.ShouldBe("hello <@99>");
    }

    [TestCase(arg1: false, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    [TestCase(arg1: true, arg2: false)]
    public void Parse_MissingReplyTrigger_LeavesDiscussionUnsent(bool reply, bool ping)
    {
        // Act
        var draft = SylvanMailReply.Parse("hello <@99>", botId: 42, replyToBot: reply, replyPingEnabled: ping);

        // Assert
        draft.ShouldBeNull();
    }

    [Test]
    public void Parse_EditedBodyMention_ReevaluatesSignedMode()
    {
        // Arrange
        var original = SylvanMailReply.Parse("<@42> hello", botId: 42, replyToBot: true, replyPingEnabled: true);

        // Act
        var edited = SylvanMailReply.Parse("hello", botId: 42, replyToBot: true, replyPingEnabled: true);

        // Assert
        original.ShouldNotBeNull().Anonymous.ShouldBeTrue();
        edited.ShouldNotBeNull().Anonymous.ShouldBeFalse();
    }

    [Test]
    public void Parse_AttachmentOnlyMention_PreservesEmptyBodyForAttachmentValidation()
    {
        // Act
        var draft = SylvanMailReply.Parse("<@42>", botId: 42, replyToBot: false, replyPingEnabled: false);

        // Assert
        draft.ShouldNotBeNull();
        draft.Anonymous.ShouldBeTrue();
        draft.Content.ShouldBeEmpty();
    }
}