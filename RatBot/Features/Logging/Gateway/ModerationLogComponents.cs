using System.Globalization;
using RatBot.Application.Features.Logging;

namespace RatBot.Features.Logging.Gateway;

internal static class ModerationLogComponents
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss'Z'";

    public static ModerationLogMessage BuildEditLog(
        string messageJumpUrl,
        ulong channelId,
        ulong authorId,
        string? beforeContent,
        string? afterContent
    )
    {
        MessageComponent components = new ComponentBuilderV2(
            new ContainerBuilder()
                .WithTextDisplay(
                    new TextDisplayBuilder().WithContent(BuildEditHeader(messageJumpUrl, channelId, authorId)))
                .WithSeparator(new SeparatorBuilder())
                .WithTextDisplay(new TextDisplayBuilder().WithContent(EditContentBlock(beforeContent, afterContent)))
        ).Build();

        return new ModerationLogMessage(components, ImmutableArray<ModerationLogAttachment>.Empty);
    }

    public static ModerationLogMessage BuildDeleteLog(
        ulong channelId,
        ulong authorId,
        string? precedingMessageJumpUrl,
        string? content,
        IReadOnlyCollection<CachedAttachmentEvidence> attachments
    )
    {
        ImmutableArray<ModerationLogAttachment> attachmentModels = BuildAttachmentModels(attachments);
        ContainerBuilder container = new ContainerBuilder()
            .WithTextDisplay(
                new TextDisplayBuilder().WithContent(BuildDeleteHeader(channelId, authorId, precedingMessageJumpUrl)))
            .WithSeparator(new SeparatorBuilder())
            .WithTextDisplay(new TextDisplayBuilder().WithContent(ContentBlock(content)));

        if (attachmentModels.Length > 0)
            AddAttachmentComponents(container, attachmentModels);

        return new ModerationLogMessage(new ComponentBuilderV2(container).Build(), attachmentModels);
    }

    public static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    public static string GetJumpUrl(IMessage message)
    {
        return message.GetJumpUrl();
    }

    private static string BuildEditHeader(string messageJumpUrl, ulong channelId, ulong authorId)
    {
        return $"**Message Edit:** [Message]({messageJumpUrl}) by <@{authorId}> (`{authorId}`) in <#{channelId}>";
    }

    private static string BuildDeleteHeader(ulong channelId, ulong authorId, string? precedingMessageJumpUrl)
    {
        string header = $"**Message Delete:** <@{authorId}> (`{authorId}`) in <#{channelId}>";

        if (!string.IsNullOrWhiteSpace(precedingMessageJumpUrl))
            header += $" • [Preceding message]({precedingMessageJumpUrl})";

        return header;
    }

    private static void AddAttachmentComponents(ContainerBuilder container,
        ImmutableArray<ModerationLogAttachment> attachments)
    {
        container.WithSeparator(new SeparatorBuilder());

        ImmutableArray<ModerationLogAttachment> mediaAttachments =
            attachments.Where(IsMediaAttachment).ToImmutableArray();

        if (mediaAttachments.Length > 0)
        {
            MediaGalleryBuilder gallery = new MediaGalleryBuilder();

            foreach (ModerationLogAttachment attachment in mediaAttachments)
                gallery.AddItem(new MediaGalleryItemProperties
                    { Media = new UnfurledMediaItemProperties(attachment.AttachmentUrl) });

            container.WithMediaGallery(gallery);
        }

        foreach (ModerationLogAttachment attachment in attachments.Where(attachment => !IsMediaAttachment(attachment)))
            container.WithFile(
                new FileComponentBuilder().WithFile(new UnfurledMediaItemProperties(attachment.AttachmentUrl)));
    }

    private static bool IsMediaAttachment(ModerationLogAttachment attachment)
    {
        return attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || attachment.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<ModerationLogAttachment> BuildAttachmentModels(
        IReadOnlyCollection<CachedAttachmentEvidence> attachments)
    {
        return attachments.Select(BuildAttachmentModel).ToImmutableArray();
    }

    private static ModerationLogAttachment BuildAttachmentModel(CachedAttachmentEvidence attachment)
    {
        string fileName = $"evidence-{attachment.Index}{GetExtension(attachment.ContentType)}";
        return new ModerationLogAttachment(fileName, attachment.ContentType, attachment.Bytes);
    }

    private static string GetExtension(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            _ => ".bin"
        };
    }

    private static string ContentBlock(string? value)
    {
        return value is null ? "*Unavailable*" : CodeBlock(value);
    }

    private static string EditContentBlock(string? beforeContent, string? afterContent)
    {
        if (beforeContent is null || afterContent is null)
            return
                $"{AnsiCodeBlock(beforeContent ?? "*Unavailable*")}\n{AnsiCodeBlock(afterContent ?? "*Unavailable*")}";

        MessageEditDiff diff = new MessageEditDiffer().BuildDiff(beforeContent, afterContent);
        return
            $"{AnsiCodeBlock(MessageEditDiffAnsiRenderer.RenderBefore(diff))}\n{AnsiCodeBlock(MessageEditDiffAnsiRenderer.RenderAfter(diff))}";
    }

    private static string CodeBlock(string value)
    {
        string sanitized = value.Replace("```", "`\u200b``", StringComparison.Ordinal);
        return $"```{sanitized}```";
    }

    private static string AnsiCodeBlock(string value)
    {
        string sanitized = value.Replace("```", "`\u200b``", StringComparison.Ordinal);
        return $"```ansi\n{sanitized}\n```";
    }

    internal sealed record ModerationLogMessage(
        MessageComponent Components,
        ImmutableArray<ModerationLogAttachment> Attachments);

    internal sealed record ModerationLogAttachment(string FileName, string ContentType, byte[] Bytes)
    {
        public string AttachmentUrl => $"attachment://{FileName}";
    }
}