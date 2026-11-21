using SylvanBot.Application.Features.SylvanMail;

namespace SylvanBot.Tests.Features.SylvanMail;

[TestFixture]
public sealed class SylvanMailReplyTests
{
    [TestCase("<@42> hello", arg2: false, arg3: false)]
    [TestCase("<@!42> hello", arg2: false, arg3: false)]
    [TestCase("<@42> hello", arg2: true, arg3: true)]
    [TestCase("<@!42> hello", arg2: true, arg3: false)]
    public void Parse_ExplicitBodyMention_PreparesDraft(string text, bool reply, bool ping)
    {
        // Act
        SylvanMailReply? draft = SylvanMailReply.Parse(content: text, botId: 42, replyToBot: reply, replyPingEnabled: ping);

        // Assert
        draft.ShouldNotBeNull();
        draft.Content.ShouldBe("hello");
    }

    [Test]
    public void Parse_PingedReplyToSylvan_PreparesDraft()
    {
        // Act
        SylvanMailReply? draft = SylvanMailReply.Parse("hello <@99>", botId: 42, replyToBot: true, replyPingEnabled: true);

        // Assert
        draft.ShouldNotBeNull();
        draft.Content.ShouldBe("hello <@99>");
    }

    [TestCase(arg1: false, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    [TestCase(arg1: true, arg2: false)]
    public void Parse_MissingReplyTrigger_LeavesDiscussionUnsent(bool reply, bool ping)
    {
        // Act
        SylvanMailReply? draft = SylvanMailReply.Parse("hello <@99>", botId: 42, replyToBot: reply, replyPingEnabled: ping);

        // Assert
        draft.ShouldBeNull();
    }

    [Test]
    public void Parse_EditedBodyMention_KeepsReplyDraft()
    {
        // Arrange
        SylvanMailReply? original = SylvanMailReply.Parse("<@42> hello", botId: 42, replyToBot: true, replyPingEnabled: true);

        // Act
        SylvanMailReply? edited = SylvanMailReply.Parse("hello", botId: 42, replyToBot: true, replyPingEnabled: true);

        // Assert
        original.ShouldNotBeNull().Content.ShouldBe("hello");
        edited.ShouldNotBeNull().Content.ShouldBe("hello");
    }

    [Test]
    public void Parse_AttachmentOnlyMention_PreservesEmptyBodyForAttachmentValidation()
    {
        // Act
        SylvanMailReply? draft = SylvanMailReply.Parse("<@42>", botId: 42, replyToBot: false, replyPingEnabled: false);

        // Assert
        draft.ShouldNotBeNull();
        draft.Content.ShouldBeEmpty();
    }
}