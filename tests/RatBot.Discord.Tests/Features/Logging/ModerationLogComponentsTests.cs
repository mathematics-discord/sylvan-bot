using Discord;
using NSubstitute;
using RatBot.Application.Features.Logging;
using RatBot.Features.Logging.Gateway;
using Shouldly;

namespace RatBot.Discord.Tests.Features.Logging;

public sealed class ModerationLogComponentsTests
{
    [Test]
    public void GetJumpUrl_UsesDiscordNetMessageJumpUrl()
    {
        ITextChannel channel = Substitute.For<ITextChannel>();
        IMessage message = Substitute.For<IMessage>();

        channel.GuildId.Returns(123UL);
        channel.Id.Returns(456UL);
        message.Channel.Returns(channel);
        message.Id.Returns(789UL);

        ModerationLogComponents.GetJumpUrl(message).ShouldBe("https://discord.com/channels/123/456/789");
    }

    [Test]
    public void BuildEditLog_RendersBeforeAndAfterUnavailableStates()
    {
        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildEditLog(
            "https://discord.com/channels/1/2/3",
            2,
            4,
            null,
            null
        );

        TextDisplays(log).Last().Content.ShouldBe("```ansi\n*Unavailable*\n```\n```ansi\n*Unavailable*\n```");
        TextDisplays(log).Last().Content.ShouldNotContain("Content");
        TextDisplays(log).Last().Content.ShouldNotContain("Before:");
        TextDisplays(log).Last().Content.ShouldNotContain("After:");
    }

    [Test]
    public void BuildEditLog_RendersMessageDiffAsAnsiBlock()
    {
        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildEditLog(
            "https://discord.com/channels/1/2/3",
            2,
            4,
            "Hayes",
            "hates"
        );

        TextDisplays(log).Last().Content
            .ShouldBe("```ansi\n\e[1;31mH\e[0ma\e[1;31my\e[0mes\n```\n```ansi\n\e[1;32mh\e[0ma\e[1;32mt\e[0mes\n```");
        TextDisplays(log).Last().Content.ShouldNotContain("Before:");
        TextDisplays(log).Last().Content.ShouldNotContain("After:");
    }

    [Test]
    public void BuildEditLog_RendersExactInlineHeader()
    {
        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildEditLog(
            "https://discord.com/channels/1/2/3",
            2,
            4,
            "Hayes",
            "hates"
        );

        TextDisplays(log).First().Content
            .ShouldBe("**Message Edit:** [Message](https://discord.com/channels/1/2/3) by <@4> (`4`) in <#2>");
    }

    [Test]
    public void BuildDeleteLog_RendersExactInlineHeaderWithPrecedingMessage()
    {
        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildDeleteLog(
            2,
            4,
            "https://discord.com/channels/1/2/3",
            "cached",
            Array.Empty<CachedAttachmentEvidence>()
        );

        TextDisplays(log)
            .First()
            .Content.ShouldBe(
                "**Message Delete:** <@4> (`4`) in <#2> • [Preceding message](https://discord.com/channels/1/2/3)");
    }

    [Test]
    public void BuildDeleteLog_RendersExactInlineHeaderWithoutPrecedingMessage()
    {
        ModerationLogComponents.ModerationLogMessage log = BuildDeleteLogWithoutPrecedingMessage();

        TextDisplays(log).First().Content.ShouldBe("**Message Delete:** <@4> (`4`) in <#2>");
    }

    [Test]
    public void BuildDeleteLog_RendersCachedContentInPlainCodeBlock()
    {
        ModerationLogComponents.ModerationLogMessage log = BuildDeleteLogWithoutPrecedingMessage();

        TextDisplays(log).Last().Content.ShouldBe("```cached```");
        TextDisplays(log).Last().Content.ShouldNotContain("Observed");
        TextDisplays(log).Last().Content.ShouldNotContain("Deleted");
    }

    [Test]
    public void BuildDeleteLog_RendersUnavailableWhenContentIsMissing()
    {
        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildDeleteLog(
            2,
            4,
            null,
            null,
            Array.Empty<CachedAttachmentEvidence>()
        );

        TextDisplays(log).Last().Content.ShouldBe("*Unavailable*");
    }

    [Test]
    public void BuildDeleteLog_OmitsAttachmentComponentsWhenEvidenceIsAbsent()
    {
        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildDeleteLog(
            2,
            4,
            null,
            null,
            Array.Empty<CachedAttachmentEvidence>()
        );

        log.Attachments.ShouldBeEmpty();
        ContainerComponents(log).OfType<MediaGalleryComponent>().ShouldBeEmpty();
        ContainerComponents(log).OfType<FileComponent>().ShouldBeEmpty();
        ContainerComponents(log).OfType<SeparatorComponent>().Count().ShouldBe(1);
    }

    [Test]
    public void BuildDeleteLog_RendersMediaAttachmentsAsGalleryItems()
    {
        CachedAttachmentEvidence image = new CachedAttachmentEvidence(3, new byte[] { 1 }, "image/png");
        CachedAttachmentEvidence video = new CachedAttachmentEvidence(4, new byte[] { 2 }, "video/mp4");

        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildDeleteLog(
            2,
            4,
            null,
            null,
            [image, video]
        );

        MediaGalleryComponent gallery = ContainerComponents(log).OfType<MediaGalleryComponent>().Single();

        log.Attachments.Select(attachment => attachment.FileName).ShouldBe(["evidence-3.png", "evidence-4.mp4"]);
        log.Attachments.Select(attachment => attachment.AttachmentUrl)
            .ShouldBe(["attachment://evidence-3.png", "attachment://evidence-4.mp4"]);
        gallery.Items.Select(item => item.Media.Url)
            .ShouldBe(["attachment://evidence-3.png", "attachment://evidence-4.mp4"]);
        gallery.Items.Select(item => item.Description).ShouldBe([null, null]);
        gallery.Items.Select(item => item.Media.Url)
            .ShouldBe(log.Attachments.Select(attachment => $"attachment://{attachment.FileName}"));
        ContainerComponents(log).OfType<FileComponent>().ShouldBeEmpty();
    }

    [Test]
    public void BuildDeleteLog_RendersNonMediaAttachmentsAsFileComponents()
    {
        CachedAttachmentEvidence file = new CachedAttachmentEvidence(4, new byte[] { 2 }, "text/plain");

        ModerationLogComponents.ModerationLogMessage log = ModerationLogComponents.BuildDeleteLog(
            2,
            4,
            null,
            null,
            [file]
        );

        FileComponent fileComponent = ContainerComponents(log).OfType<FileComponent>().Single();

        log.Attachments.Select(attachment => attachment.FileName).ShouldBe(["evidence-4.bin"]);
        log.Attachments.Select(attachment => attachment.AttachmentUrl).ShouldBe(["attachment://evidence-4.bin"]);
        fileComponent.File.Url.ShouldBe("attachment://evidence-4.bin");
        fileComponent.File.Url.ShouldBe($"attachment://{log.Attachments.Single().FileName}");
        ContainerComponents(log).OfType<MediaGalleryComponent>().ShouldBeEmpty();
    }

    private static ModerationLogComponents.ModerationLogMessage BuildDeleteLogWithoutPrecedingMessage()
    {
        return ModerationLogComponents.BuildDeleteLog(2, 4, null, "cached", Array.Empty<CachedAttachmentEvidence>());
    }

    private static IReadOnlyCollection<IMessageComponent> ContainerComponents(
        ModerationLogComponents.ModerationLogMessage log)
    {
        ContainerComponent container =
            log.Components.Components.ShouldHaveSingleItem().ShouldBeOfType<ContainerComponent>();

        return container.Components;
    }

    private static IReadOnlyCollection<TextDisplayComponent> TextDisplays(
        ModerationLogComponents.ModerationLogMessage log)
    {
        return ContainerComponents(log).OfType<TextDisplayComponent>().ToArray();
    }
}