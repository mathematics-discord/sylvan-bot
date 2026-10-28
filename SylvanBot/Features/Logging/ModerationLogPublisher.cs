using SylvanBot.Application.Features.MessageCache;

namespace SylvanBot.Features.Logging;

using DotNext;

using DownloadedAttachment = ModerationLogComponents.DownloadedAttachment;

public sealed class ModerationLogPublisher(
    LoggingScopeResolver scope,
    ModerationAuditLookup audit,
    HttpClient httpClient,
    IOptions<LoggingOptions> options,
    ILogger logger
)
{
    private const MessageFlags CompactLogMessageFlags = MessageFlags.ComponentsV2 | MessageFlags.SuppressNotification;
    private const int MaxUploadAttachmentCount = 10;
    private const long MaxUploadAttachmentBytes = 8 * 1024 * 1024;

    private readonly ILogger _logger = logger.ForContext<ModerationLogPublisher>();
    private readonly LoggingOptions _options = options.Value;

    private static async Task SendLogAsync(
        IMessageChannel logChannel,
        IReadOnlyCollection<DownloadedAttachment> attachments,
        CancellationToken ct,
        string? text = null,
        MessageComponent? components = null)
    {
        ct.ThrowIfCancellationRequested();
        var flags = components is null ? MessageFlags.SuppressNotification : CompactLogMessageFlags;
        var request = new RequestOptions { CancelToken = ct };

        if (attachments.Count == 0)
        {
            await logChannel.SendMessageAsync(
                text: text,
                options: request,
                allowedMentions: AllowedMentions.None,
                components: components,
                flags: flags);

            return;
        }

        List<FileAttachment> files = [];

        try
        {
            foreach (var attachment in attachments)
                files.Add(new FileAttachment(new MemoryStream(buffer: attachment.Bytes, writable: false), fileName: attachment.FileName));

            await logChannel.SendFilesAsync(
                attachments: files,
                text: text,
                options: request,
                allowedMentions: AllowedMentions.None,
                components: components,
                flags: flags);
        }
        finally
        {
            foreach (var file in files)
                file.Dispose();
        }
    }

    public async Task PublishEditAsync(
        ulong guildId,
        IChannel sourceChannel,
        ulong authorId,
        string messageJumpUrl,
        string? beforeContent,
        string afterContent,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var logChannel = await scope.GetLogChannelAsync(guildId: guildId, sourceChannel: sourceChannel, forEdits: true, ct: ct);

        if (logChannel is null || expiresAtUtc <= DateTimeOffset.UtcNow)
            return;

        var compactLog = ModerationLogComponents.BuildEditLog(
            messageJumpUrl: messageJumpUrl,
            channelId: sourceChannel.Id,
            authorId: authorId,
            beforeContent: beforeContent,
            afterContent: afterContent);
        await SendLogAsync(logChannel: logChannel, attachments: compactLog.Attachments, ct: ct, components: compactLog.Components);
    }

    public async Task<bool> PublishDeletionAsync(
        ulong guildId,
        IMessageChannel sourceChannel,
        CachedMessageSnapshot deletedMessage,
        CancellationToken ct)
    {
        var messageId = deletedMessage.MessageId;

        ct.ThrowIfCancellationRequested();
        var logChannel = await scope.GetLogChannelAsync(guildId: guildId, sourceChannel: sourceChannel, forEdits: false, ct: ct);

        if (logChannel is null)
            return false;

        List<DownloadedAttachment> attachments = [];
        var precedingMessageJumpUrl = await TryGetPrecedingMessageJumpUrlAsync(sourceChannel: sourceChannel, deletedMessageId: messageId, ct: ct);
        int omittedAttachmentCount;

        using (var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            downloadTimeout.CancelAfter(TimeSpan.FromSeconds(10));

            omittedAttachmentCount = await DownloadAttachmentsAsync(
                deletedMessage: deletedMessage,
                attachments: attachments,
                ct: downloadTimeout.Token);
        }

        ct.ThrowIfCancellationRequested();

        var compactLog = ModerationLogComponents.BuildDeleteLog(
            channelId: sourceChannel.Id,
            authorId: deletedMessage.AuthorId,
            precedingMessageJumpUrl: precedingMessageJumpUrl,
            content: deletedMessage.Content,
            attachments: attachments,
            omittedAttachmentCount: omittedAttachmentCount);

        if (deletedMessage.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        await SendLogAsync(logChannel: logChannel, attachments: compactLog.Attachments, ct: ct, components: compactLog.Components);

        return true;
    }

    public async Task<bool> PublishBulkDeletionAsync(
        IReadOnlyCollection<CachedMessageSnapshot> messages,
        IMessageChannel sourceChannel,
        int originalMessageCount,
        ulong? soleAuthorId,
        DateTimeOffset deletedAt,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (sourceChannel is not IGuildChannel guildChannel)
            return false;

        var logChannel = await scope.GetLogChannelAsync(guildId: guildChannel.GuildId, sourceChannel: sourceChannel, forEdits: false, ct: ct);

        if (logChannel is null)
            return false;

        var moderatorId = await audit.TryGetBulkDeleteModeratorAsync(
            guildId: guildChannel.GuildId,
            channelId: sourceChannel.Id,
            originalMessageCount: originalMessageCount,
            soleAuthorId: soleAuthorId,
            deletedAt: deletedAt,
            ct: ct);
        var timestamp = ModerationLogComponents.FormatTimestamp(deletedAt);

        var text = moderatorId is { } actorId
            ? $"Bulk delete in <#{sourceChannel.Id}> by <@{actorId}> at {timestamp}."
            : $"Bulk delete in <#{sourceChannel.Id}> at {timestamp}. Moderator could not be determined.";

        ImmutableArray<(ulong AuthorId, ulong ChannelId, string Content)> rows = messages
            .Where(message => message.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .Select(message => (message.AuthorId, message.ChannelId, message.Content))
            .ToImmutableArray();

        if (rows.IsEmpty)
            return false;

        var transcript = new DownloadedAttachment("bulk-delete.jsonl", "application/x-ndjson", BulkDeleteTranscript.Create(rows));

        await SendLogAsync(logChannel: logChannel, [transcript], ct: ct, text: text);

        return true;
    }

    private async Task<int> DownloadAttachmentsAsync(
        CachedMessageSnapshot deletedMessage,
        List<DownloadedAttachment> attachments,
        CancellationToken ct)
    {
        var initialAttachmentCount = attachments.Count;

        var uploadedBytes = attachments.Sum(attachment => attachment.Bytes.LongLength);

        var remainingBytes = MaxUploadAttachmentBytes - uploadedBytes;

        IEnumerable<MessageAttachment> candidates = deletedMessage.Attachments
            .Take(_options.MaxAttachmentCountPerMessage);

        foreach (var attachment in candidates)
        {
            var uploadLimitReached = attachments.Count >= MaxUploadAttachmentCount || remainingBytes <= 0 || ct.IsCancellationRequested;

            if (uploadLimitReached)
                break;

            var maxBytes = Math.Min(val1: _options.MaxAttachmentBytesPerAttachment, val2: remainingBytes);
            var url = string.IsNullOrWhiteSpace(attachment.Url) ? attachment.ProxyUrl : attachment.Url;
            var cannotDownload = attachment.Size > maxBytes || string.IsNullOrWhiteSpace(url);

            if (cannotDownload || string.IsNullOrWhiteSpace(url))
                continue;

            try
            {
                Optional<byte[]> downloadResult = await AttachmentEvidenceDownloader.TryDownloadAsync(
                    httpClient: httpClient,
                    url: url,
                    maxBytes: maxBytes,
                    logger: _logger,
                    ct: ct);

                if (downloadResult is { HasValue: true, Value: var bytes })
                {
                    var contentType = attachment.ContentType ?? "application/octet-stream";
                    var fileName = ModerationLogComponents.GetAttachmentFileName(attachments.Count + 1, contentType: contentType);
                    attachments.Add(new DownloadedAttachment(FileName: fileName, ContentType: contentType, Bytes: bytes));
                    remainingBytes -= bytes.LongLength;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.Debug("Attachment download cancelled for deleted message {MessageId}", propertyValue: deletedMessage.MessageId);

                break;
            }
        }

        return deletedMessage.Attachments.Length - (attachments.Count - initialAttachmentCount);
    }

    private async Task<string?> TryGetPrecedingMessageJumpUrlAsync(IMessageChannel sourceChannel, ulong deletedMessageId, CancellationToken ct)
    {
        using var lookupTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookupTimeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var request = new RequestOptions { CancelToken = lookupTimeout.Token };

            IEnumerable<IMessage> messages = await sourceChannel
                .GetMessagesAsync(fromMessageId: deletedMessageId, dir: Direction.Before, limit: 1, options: request)
                .FlattenAsync();

            var precedingMessage = messages.FirstOrDefault();

            return precedingMessage?.GetJumpUrl();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (lookupTimeout.IsCancellationRequested)
        {
            _logger.Debug("Preceding message lookup timed out for deleted message {MessageId}", propertyValue: deletedMessageId);

            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug(exception: ex, "Failed to fetch preceding message for deleted message {MessageId}", propertyValue: deletedMessageId);

            return null;
        }
    }
}