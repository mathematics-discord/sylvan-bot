using System.Globalization;

namespace SylvanBot.Features.Logging;

internal static class ModerationLogComponents
{
    private const string TimestampFormat        = "yyyy-MM-dd HH:mm:ss'Z'";
    private const int    MaxComponentTextLength = 4000;

    public static ModerationLogMessage BuildEditLog(
        string messageJumpUrl,
        ulong channelId,
        ulong authorId,
        string? beforeContent,
        string? afterContent
    )
    {
        var header = $"**Message Edit:** [Message]({messageJumpUrl}) by <@{authorId}> (`{authorId}`) in <#{channelId}>";
        var before = beforeContent ?? "*Unavailable*";
        var after  = afterContent ?? "*Unavailable*";

        if (beforeContent is not null && afterContent is not null)
            (before, after) = MessageEditDiffer.BuildDiff(before: beforeContent, after: afterContent);

        var blockLength = (MaxComponentTextLength - header.Length - 1) / 2;
        var content     = $"{CodeBlock(value: before, maxLength: blockLength, ansi: true)}\n{CodeBlock(value: after, maxLength: blockLength, ansi: true)}";

        var components = new ComponentBuilderV2(
            new ContainerBuilder()
               .WithTextDisplay(new TextDisplayBuilder().WithContent(header))
               .WithSeparator(new SeparatorBuilder())
               .WithTextDisplay(new TextDisplayBuilder().WithContent(content))
        ).Build();

        return new ModerationLogMessage(Components: components, Attachments: ImmutableArray<DownloadedAttachment>.Empty);
    }

    public static ModerationLogMessage BuildDeleteLog(
        ulong channelId,
        ulong authorId,
        string? precedingMessageJumpUrl,
        string content,
        IReadOnlyCollection<DownloadedAttachment> attachments,
        int omittedAttachmentCount = 0
    )
    {
        var author = $"<@{authorId}> (`{authorId}`)";
        var header = $"**Message Delete:** {author} in <#{channelId}>";

        if (!string.IsNullOrWhiteSpace(precedingMessageJumpUrl))
            header += $" • [Preceding message]({precedingMessageJumpUrl})";

        var attachmentNotice = omittedAttachmentCount > 0
            ? $"\n*{omittedAttachmentCount} attachment(s) omitted (download failures or limits).*"
            : string.Empty;

        var contentLength = MaxComponentTextLength - header.Length - attachmentNotice.Length;
        var contentBlock  = CodeBlock(value: content, maxLength: contentLength, ansi: false);

        var container = new ContainerBuilder()
           .WithTextDisplay(new TextDisplayBuilder().WithContent(header))
           .WithSeparator(new SeparatorBuilder())
           .WithTextDisplay(new TextDisplayBuilder().WithContent(contentBlock + attachmentNotice));

        if (attachments.Count > 0)
            AddAttachmentComponents(container: container, attachments: attachments);

        return new ModerationLogMessage(new ComponentBuilderV2(container).Build(), Attachments: attachments);
    }

    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString(format: TimestampFormat, formatProvider: CultureInfo.InvariantCulture);

    public static string GetAttachmentFileName(int index, string contentType)
        => string.Format(
            provider: CultureInfo.InvariantCulture,
            "evidence-{0}{1}",
            arg0: index,
            contentType.ToLowerInvariant() switch
            {
                "image/jpeg"      => ".jpg",
                "image/png"       => ".png",
                "image/gif"       => ".gif",
                "image/webp"      => ".webp",
                "video/mp4"       => ".mp4",
                "video/webm"      => ".webm",
                "video/quicktime" => ".mov",
                _                 => ".bin",
            });

    private static void AddAttachmentComponents(
        ContainerBuilder container,
        IReadOnlyCollection<DownloadedAttachment> attachments)
    {
        container.WithSeparator(new SeparatorBuilder());

        ImmutableArray<DownloadedAttachment> mediaAttachments = attachments
           .Where(IsMediaAttachment)
           .ToImmutableArray();

        if (mediaAttachments.Length > 0)
        {
            var gallery = new MediaGalleryBuilder();

            foreach (var attachment in mediaAttachments)
                gallery.AddItem(
                    new MediaGalleryItemProperties
                    {
                        Media = new UnfurledMediaItemProperties(attachment.Url),
                    });

            container.WithMediaGallery(gallery);
        }

        IEnumerable<DownloadedAttachment> fileAttachments = attachments
           .Where(attachment => !IsMediaAttachment(attachment));

        foreach (var attachment in fileAttachments)
            container.WithFile(new FileComponentBuilder().WithFile(new UnfurledMediaItemProperties(attachment.Url)));
    }

    private static bool IsMediaAttachment(DownloadedAttachment attachment)
        => attachment.ContentType.StartsWith("image/", comparisonType: StringComparison.OrdinalIgnoreCase)
            || attachment.ContentType.StartsWith("video/", comparisonType: StringComparison.OrdinalIgnoreCase);

    private static string CodeBlock(string value, int maxLength, bool ansi)
    {
        var opening       = ansi ? "```ansi\n" : "```";
        var closing       = ansi ? "\n```" : "```";
        var sanitized     = value.Replace("```", "`\u200b``", comparisonType: StringComparison.Ordinal);
        var contentLength = maxLength - opening.Length - closing.Length;

        if (sanitized.Length <= contentLength)
            return $"{opening}{sanitized}{closing}";

        const string notice       = "\n[Content truncated]";
        var          reset        = ansi ? "\e[0m" : string.Empty;
        var          prefixLength = contentLength - notice.Length - reset.Length;

        if (ansi)
        {
            var escapeStart        = sanitized.LastIndexOf(value: '\e', prefixLength - 1);
            var cutsEscapeSequence = escapeStart >= 0 && sanitized.IndexOf(value: 'm', startIndex: escapeStart) >= prefixLength;

            if (cutsEscapeSequence)
                prefixLength = escapeStart;
        }

        var cutsSurrogatePair = prefixLength > 0 && char.IsHighSurrogate(sanitized[prefixLength - 1]);

        if (cutsSurrogatePair)
            prefixLength--;

        sanitized = $"{sanitized.AsSpan(start: 0, length: prefixLength)}{reset}{notice}";

        return $"{opening}{sanitized}{closing}";
    }

    internal sealed record ModerationLogMessage(
        MessageComponent Components,
        IReadOnlyCollection<DownloadedAttachment> Attachments);

    internal sealed record DownloadedAttachment(string FileName, string ContentType, byte[] Bytes)
    {
        public string Url => $"attachment://{FileName}";
    }
}