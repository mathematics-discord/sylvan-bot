namespace RatBot.Tests.Features.Logging;

using Discord;
using RatBot.Features.Logging;
using DownloadedAttachment = RatBot.Features.Logging.ModerationLogComponents.DownloadedAttachment;

public sealed class ModerationLogComponentsTests
{
    [TestCase(null, null, "```ansi\n*Unavailable*\n```\n```ansi\n*Unavailable*\n```")]
    [TestCase("", "", "```ansi\n\n```\n```ansi\n\n```")]
    public void BuildEditLog_UnknownOrEmptyVersions_PreservesAvailability(
        string? beforeContent,
        string? afterContent,
        string expected)
    {
        // Arrange
        const string jumpUrl = "https://discord.com/channels/1/2/3";

        // Act
        var log = ModerationLogComponents.BuildEditLog(
            jumpUrl,
            2,
            4,
            beforeContent,
            afterContent);

        // Assert
        TextDisplays(log).Last().Content.ShouldBe(expected);
        TextDisplays(log).Last().Content.ShouldNotContain("Content");
        TextDisplays(log).Last().Content.ShouldNotContain("Before:");
        TextDisplays(log).Last().Content.ShouldNotContain("After:");
    }

    [Test]
    public void BuildEditLog_RendersMessageDiffAsAnsiBlock()
    {
        var log = ModerationLogComponents.BuildEditLog(
            "https://discord.com/channels/1/2/3",
            2,
            4,
            "Hayes",
            "hates");

        TextDisplays(log).Last().Content
            .ShouldBe("```ansi\n\e[1;31mH\e[0ma\e[1;31my\e[0mes\n```\n```ansi\n\e[1;32mh\e[0ma\e[1;32mt\e[0mes\n```");
        TextDisplays(log).Last().Content.ShouldNotContain("Before:");
        TextDisplays(log).Last().Content.ShouldNotContain("After:");
    }

    [Test]
    public void BuildEditLog_RendersExactInlineHeader()
    {
        var log = ModerationLogComponents.BuildEditLog(
            "https://discord.com/channels/1/2/3",
            2,
            4,
            "Hayes",
            "hates");

        TextDisplays(log).First().Content
            .ShouldBe("**Message Edit:** [Message](https://discord.com/channels/1/2/3) by <@4> (`4`) in <#2>");
    }

    [Test]
    public void BuildDeleteLog_RendersExactInlineHeaderWithPrecedingMessage()
    {
        var log = ModerationLogComponents.BuildDeleteLog(
            2,
            3,
            4,
            "https://discord.com/channels/1/2/3",
            "cached",
            []);

        TextDisplays(log)
            .First()
            .Content.ShouldBe(
                "**Message Delete:** <@4> (`4`) in <#2> • [Preceding message](https://discord.com/channels/1/2/3)");
    }

    [Test]
    public void BuildDeleteLog_RendersExactInlineHeaderWithoutPrecedingMessage()
    {
        var log = BuildDeleteLogWithoutPrecedingMessage();

        TextDisplays(log).First().Content.ShouldBe("**Message Delete:** <@4> (`4`) in <#2>");
    }

    [TestCase("cached", "```cached```")]
    [TestCase("", "``````")]
    [TestCase("first\nsecond", "```first\nsecond```")]
    [TestCase("```", "````\u200b`````")]
    public void BuildDeleteLog_KnownContent_PreservesLiteralContentInCodeBlock(string content, string expected)
    {
        // Arrange
        const ulong channelId = 2;

        // Act
        var log = ModerationLogComponents.BuildDeleteLog(channelId, 3, 4, null, content, []);

        // Assert
        TextDisplays(log).Last().Content.ShouldBe(expected);
        TextDisplays(log).Last().Content.ShouldNotContain("Observed");
        TextDisplays(log).Last().Content.ShouldNotContain("Deleted");
    }

    [Test]
    public void BuildEditLog_ExpandedAnsiOutput_TruncatesSafelyWithinTextLimit()
    {
        // Arrange
        var before = string.Concat(Enumerable.Repeat("Ha", count: 500));
        var after = string.Concat(Enumerable.Repeat("ha", count: 500));

        // Act
        var log = ModerationLogComponents.BuildEditLog("https://discord.com/channels/1/2/3", 2, 4, before, after);

        // Assert
        IReadOnlyCollection<TextDisplayComponent> textDisplays = TextDisplays(log);

        var totalTextLength = textDisplays.Sum(display => display.Content.Length);

        var content = textDisplays.Last().Content;
        totalTextLength.ShouldBeLessThanOrEqualTo(4000);
        content.ShouldContain("[Content truncated]");
        content.ShouldStartWith("```ansi\n");
        content.ShouldContain("\n```\n```ansi\n");
        content.ShouldEndWith("\n```");

        var withoutColors = content
            .Replace("\e[1;31m", string.Empty, StringComparison.Ordinal)
            .Replace("\e[1;32m", string.Empty, StringComparison.Ordinal)
            .Replace("\e[0m", string.Empty, StringComparison.Ordinal);

        withoutColors.ShouldNotContain("\e");
    }

    [Test]
    public void BuildDeleteLog_RendersUnavailableWhenContentIsMissing()
    {
        var log = ModerationLogComponents.BuildDeleteLog(
            2,
            3,
            4,
            null,
            null,
            []);

        TextDisplays(log).Last().Content.ShouldBe("*Unavailable*");
    }

    [Test]
    public void BuildDeleteLog_OmitsAttachmentComponentsWhenEvidenceIsAbsent()
    {
        var log = ModerationLogComponents.BuildDeleteLog(
            2,
            3,
            4,
            null,
            null,
            []);

        log.Attachments.ShouldBeEmpty();
        ContainerComponents(log).OfType<MediaGalleryComponent>().ShouldBeEmpty();
        ContainerComponents(log).OfType<FileComponent>().ShouldBeEmpty();
        ContainerComponents(log).OfType<SeparatorComponent>().Count().ShouldBe(1);
    }

    [Test]
    public void BuildDeleteLog_RendersMediaAttachmentsAsGalleryItems()
    {
        var image = new DownloadedAttachment("evidence-3.png", "image/png", new byte[] { 1 });
        var video = new DownloadedAttachment("evidence-4.mp4", "video/mp4", new byte[] { 2 });

        var log = ModerationLogComponents.BuildDeleteLog(
            2,
            3,
            4,
            null,
            null,
            [image, video]);

        var gallery = ContainerComponents(log).OfType<MediaGalleryComponent>().Single();

        log.Attachments.Select(attachment => attachment.FileName).ShouldBe(["evidence-3.png", "evidence-4.mp4"]);

        log.Attachments.Select(attachment => attachment.Url)
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
        var file = new DownloadedAttachment("evidence-4.bin", "text/plain", new byte[] { 2 });

        var log = ModerationLogComponents.BuildDeleteLog(
            2,
            3,
            4,
            null,
            null,
            [file]);

        var fileComponent = ContainerComponents(log).OfType<FileComponent>().Single();

        log.Attachments.Select(attachment => attachment.FileName).ShouldBe(["evidence-4.bin"]);
        log.Attachments.Select(attachment => attachment.Url).ShouldBe(["attachment://evidence-4.bin"]);
        fileComponent.File.Url.ShouldBe("attachment://evidence-4.bin");
        fileComponent.File.Url.ShouldBe($"attachment://{log.Attachments.Single().FileName}");
        ContainerComponents(log).OfType<MediaGalleryComponent>().ShouldBeEmpty();
    }

    private static ModerationLogComponents.ModerationLogMessage BuildDeleteLogWithoutPrecedingMessage()
    {
        return ModerationLogComponents.BuildDeleteLog(2, 3, 4, null, "cached", []);
    }

    private static IReadOnlyCollection<IMessageComponent> ContainerComponents(
        ModerationLogComponents.ModerationLogMessage log)
    {
        var container =
            log.Components.Components.ShouldHaveSingleItem().ShouldBeOfType<ContainerComponent>();

        return container.Components;
    }

    private static IReadOnlyCollection<TextDisplayComponent> TextDisplays(
        ModerationLogComponents.ModerationLogMessage log)
    {
        return ContainerComponents(log).OfType<TextDisplayComponent>().ToArray();
    }
}