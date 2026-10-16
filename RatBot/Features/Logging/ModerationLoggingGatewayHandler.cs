using Microsoft.EntityFrameworkCore;
using RatBot.Gateway;
using RatBot.Infrastructure.Data;
using DownloadedAttachment = RatBot.Features.Logging.ModerationLogComponents.DownloadedAttachment;

namespace RatBot.Features.Logging;

public sealed class ModerationLoggingGatewayHandler(
    DiscordSocketClient discordClient,
    IDbContextFactory<BotDbContext> dbContextFactory,
    HttpClient httpClient,
    IOptions<LoggingOptions> options,
    ILogger logger
) : IDiscordGatewayHandler, IDisposable
{
    private const MessageFlags CompactLogMessageFlags = MessageFlags.ComponentsV2 | MessageFlags.SuppressNotification;
    private const int MaxUploadAttachmentCount = 10;
    private const long MaxUploadAttachmentBytes = 8 * 1024 * 1024;
    private const int MaxBulkDetailCount = 20;
    private const int MaxBulkDetailLength = 1500;

    private readonly ILogger _logger = logger.ForContext<ModerationLoggingGatewayHandler>();
    private readonly LoggingOptions _options = options.Value;
    private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
    private CancellationToken _shutdownToken;

    public Task InitializeAsync(CancellationToken ct)
    {
        _shutdownToken = _shutdown.Token;
        discordClient.MessageUpdated += DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted += DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted += DispatchMessagesBulkDeletedAsync;
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.MessageUpdated -= DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted -= DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted -= DispatchMessagesBulkDeletedAsync;
        _shutdown.Cancel();
    }

    public void Dispose()
    {
        Unsubscribe();
        _shutdown.Dispose();
    }

    private Task DispatchMessageUpdatedAsync(
        Cacheable<IMessage, ulong> cachedMessage,
        SocketMessage updatedMessage,
        ISocketMessageChannel sourceChannel
    )
    {
        try
        {
            var isQualifyingUpdate = updatedMessage is SocketUserMessage && IsQualifyingUserMessage(updatedMessage);

            if (!isQualifyingUpdate)
                return Task.CompletedTask;

            if (sourceChannel is not IGuildChannel guildChannel)
                return Task.CompletedTask;

            var beforeContent = cachedMessage.HasValue ? cachedMessage.Value.Content : null;
            var afterContent = updatedMessage.Content;
            var authorId = updatedMessage.Author.Id;
            var messageId = updatedMessage.Id;
            var guildId = guildChannel.GuildId;
            var messageJumpUrl = updatedMessage.GetJumpUrl();
            var ct = _shutdownToken;
            var contentIsUnchanged = cachedMessage.HasValue
                && string.Equals(beforeContent, afterContent, StringComparison.Ordinal);

            if (contentIsUnchanged)
                return Task.CompletedTask;

            _ = Task.Run(() => HandleMessageUpdatedAsync(
                guildId, sourceChannel, messageId, authorId, messageJumpUrl, beforeContent, afterContent, ct));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to dispatch message edit {MessageId} in channel {ChannelId}",
                updatedMessage.Id, sourceChannel.Id);
        }

        return Task.CompletedTask;
    }

    private Task DispatchMessageDeletedAsync(
        Cacheable<IMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        try
        {
            var sourceChannel = cachedChannel.HasValue
                ? cachedChannel.Value
                : discordClient.GetChannel(cachedChannel.Id) as IMessageChannel;

            if (sourceChannel is not IGuildChannel guildChannel)
            {
                _logger.Debug(
                    "Skipping moderation log for deleted message {MessageId}: guild channel {ChannelId} is unavailable",
                    cachedMessage.Id,
                    cachedChannel.Id);

                return Task.CompletedTask;
            }

            var deletedMessage = cachedMessage.HasValue ? cachedMessage.Value : null;
            var isKnownNonUserMessage = deletedMessage is not null && !IsQualifyingUserMessage(deletedMessage);

            if (isKnownNonUserMessage)
                return Task.CompletedTask;

            var authorId = deletedMessage?.Author.Id;
            var content = deletedMessage?.Content;
            var guildId = guildChannel.GuildId;
            var messageId = cachedMessage.Id;
            var ct = _shutdownToken;
            _ = Task.Run(() => HandleMessageDeletedAsync(
                guildId, sourceChannel, deletedMessage, messageId, authorId, content, ct));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to dispatch message deletion {MessageId} in channel {ChannelId}",
                cachedMessage.Id, cachedChannel.Id);
        }

        return Task.CompletedTask;
    }

    private Task DispatchMessagesBulkDeletedAsync(
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        try
        {
            var deletedMessages = messages.ToArray();
            var ct = _shutdownToken;
            _ = Task.Run(() => HandleMessagesBulkDeletedAsync(deletedMessages, cachedChannel, ct));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to dispatch bulk message deletion in channel {ChannelId}", cachedChannel.Id);
        }

        return Task.CompletedTask;
    }

    private async Task HandleMessageUpdatedAsync(
        ulong guildId,
        IChannel sourceChannel,
        ulong messageId,
        ulong authorId,
        string messageJumpUrl,
        string? beforeContent,
        string? afterContent,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var logChannel = await GetLogChannelAsync(guildId, sourceChannel, forEdits: true, ct);

            if (logChannel is null)
                return;

            var compactLog = ModerationLogComponents.BuildEditLog(
                messageJumpUrl,
                sourceChannel.Id,
                authorId,
                beforeContent,
                afterContent);

            await SendLogAsync(logChannel, compactLog.Attachments, ct, components: compactLog.Components);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Debug("Cancelled moderation log for edited message {MessageId} during shutdown", messageId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to log message edit for {MessageId} in channel {ChannelId}",
                messageId,
                sourceChannel.Id);
        }
    }

    private async Task HandleMessageDeletedAsync(
        ulong guildId,
        IMessageChannel sourceChannel,
        IMessage? deletedMessage,
        ulong messageId,
        ulong? authorId,
        string? content,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var logChannel = await GetLogChannelAsync(guildId, sourceChannel, forEdits: false, ct);

            if (logChannel is null)
                return;

            List<DownloadedAttachment> attachments = [];
            var precedingMessageJumpUrl = await TryGetPrecedingMessageJumpUrlAsync(sourceChannel, messageId, ct);
            var omittedAttachmentCount = 0;

            using (var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                downloadTimeout.CancelAfter(TimeSpan.FromSeconds(10));

                if (deletedMessage is not null)
                    omittedAttachmentCount = await DownloadAttachmentsAsync(deletedMessage, attachments, downloadTimeout.Token);
            }

            var compactLog = ModerationLogComponents.BuildDeleteLog(
                sourceChannel.Id,
                messageId,
                authorId,
                precedingMessageJumpUrl,
                content,
                attachments,
                omittedAttachmentCount);

            await SendLogAsync(logChannel, compactLog.Attachments, ct, components: compactLog.Components);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Debug("Cancelled moderation log for deleted message {MessageId} during shutdown", messageId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to log message deletion for {MessageId} in channel {ChannelId}",
                messageId,
                sourceChannel.Id);
        }
    }

    private async Task HandleMessagesBulkDeletedAsync(
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var sourceChannel = cachedChannel.HasValue
                ? cachedChannel.Value
                : discordClient.GetChannel(cachedChannel.Id) as IMessageChannel;

            if (sourceChannel is not IGuildChannel guildChannel)
            {
                _logger.Debug(
                    "Skipping moderation log for {MessageCount} bulk deletions: guild channel {ChannelId} is unavailable",
                    messages.Count,
                    cachedChannel.Id);

                return;
            }

            var qualifyingMessages = messages
                .Where(message => !message.HasValue || IsQualifyingUserMessage(message.Value))
                .ToArray();

            if (qualifyingMessages.Length == 0)
                return;

            var text = BuildBulkDeleteText(sourceChannel.Id, qualifyingMessages);
            var logChannel = await GetLogChannelAsync(guildChannel.GuildId, sourceChannel, forEdits: false, ct);

            if (logChannel is null)
                return;

            List<DownloadedAttachment> attachments = [];
            var omittedAttachmentCount = 0;

            using (var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                downloadTimeout.CancelAfter(TimeSpan.FromSeconds(10));

                foreach (var deletedMessage in qualifyingMessages)
                {
                    if (deletedMessage.HasValue)
                        omittedAttachmentCount += await DownloadAttachmentsAsync(deletedMessage.Value, attachments, downloadTimeout.Token);
                }
            }

            if (omittedAttachmentCount > 0)
                text += $"\n- {omittedAttachmentCount} attachment(s) unavailable or omitted (download/upload limits).";

            await SendLogAsync(logChannel, attachments, ct, text: text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Debug("Cancelled moderation log for bulk deletion in channel {ChannelId} during shutdown", cachedChannel.Id);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to log bulk message deletion in channel {ChannelId}", cachedChannel.Id);
        }
    }

    private async Task<int> DownloadAttachmentsAsync(
        IMessage deletedMessage,
        List<DownloadedAttachment> attachments,
        CancellationToken ct)
    {
        var initialAttachmentCount = attachments.Count;
        var uploadedBytes = attachments
            .Sum(attachment => attachment.Bytes.LongLength);

        var remainingBytes = MaxUploadAttachmentBytes - uploadedBytes;
        IEnumerable<IAttachment> candidates = deletedMessage.Attachments
            .Take(_options.MaxAttachmentCountPerMessage);

        foreach (var attachment in candidates)
        {
            var uploadLimitReached = attachments.Count >= MaxUploadAttachmentCount
                                     || remainingBytes <= 0
                                     || ct.IsCancellationRequested;

            if (uploadLimitReached)
                break;

            var maxBytes = Math.Min(_options.MaxAttachmentBytesPerAttachment, remainingBytes);
            var url = string.IsNullOrWhiteSpace(attachment.Url) ? attachment.ProxyUrl : attachment.Url;
            var cannotDownload = attachment.Size > maxBytes || string.IsNullOrWhiteSpace(url);

            if (cannotDownload)
                continue;

            try
            {
                Option<byte[]> downloadResult = await AttachmentEvidenceDownloader.TryDownloadAsync(
                    httpClient,
                    url,
                    maxBytes,
                    _logger,
                    ct);

                if (downloadResult.Value is Some<byte[]>(var bytes))
                {
                    var contentType = attachment.ContentType ?? "application/octet-stream";
                    var fileName = ModerationLogComponents.GetAttachmentFileName(attachments.Count + 1, contentType);
                    attachments.Add(new DownloadedAttachment(fileName, contentType, bytes));
                    remainingBytes -= bytes.LongLength;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.Debug("Attachment download cancelled for deleted message {MessageId}", deletedMessage.Id);
                break;
            }
        }

        return deletedMessage.Attachments.Count - (attachments.Count - initialAttachmentCount);
    }

    private async Task<IMessageChannel?> GetLogChannelAsync(
        ulong guildId, IChannel sourceChannel, bool forEdits, CancellationToken ct)
    {
        List<ulong> scopeIds = [sourceChannel.Id];
        var parentChannel = sourceChannel;

        if (sourceChannel is SocketThreadChannel threadChannel)
        {
            parentChannel = threadChannel.ParentChannel;
            scopeIds.Add(parentChannel.Id);
        }

        if (parentChannel is INestedChannel { CategoryId: { } categoryId })
            scopeIds.Add(categoryId);

        ulong? destinationChannelId;

        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var configuration = await dbContext.LoggingConfigurations
                .AsNoTracking()
                .SingleOrDefaultAsync(configuration => configuration.GuildId == guildId, ct);

            if (configuration?.Enabled != true)
                return null;

            destinationChannelId = forEdits
                ? configuration.EditLogChannelId
                : configuration.DeleteLogChannelId;

            if (destinationChannelId is null)
                return null;

            var isOutputChannel = scopeIds.Contains(configuration.DeleteLogChannelId ?? 0)
                                  || scopeIds.Contains(configuration.EditLogChannelId ?? 0);

            if (isOutputChannel)
                return null;

            var isExcluded = await dbContext.LoggingExcludedChannels
                .AsNoTracking()
                .AnyAsync(excluded => excluded.GuildId == guildId && scopeIds.Contains(excluded.ChannelId), ct);

            if (isExcluded)
                return null;
        }

        var logChannel = discordClient.GetChannel(destinationChannelId.Value) as IMessageChannel;
        var belongsToGuild = logChannel is IGuildChannel guildChannel && guildChannel.GuildId == guildId;

        if (belongsToGuild)
            return logChannel;

        _logger.Warning(
            "Moderation log channel {ChannelId} is unavailable in guild {GuildId} for {EventKind} in channel {SourceChannelId}",
            destinationChannelId,
            guildId,
            forEdits ? "edit" : "delete",
            sourceChannel.Id);

        return null;
    }

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
                text,
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
            {
                files.Add(new FileAttachment(
                    new MemoryStream(attachment.Bytes, writable: false), attachment.FileName));
            }

            await logChannel.SendFilesAsync(
                files,
                text,
                options: request,
                components: components,
                allowedMentions: AllowedMentions.None,
                flags: flags);
        }
        finally
        {
            foreach (var file in files)
                file.Dispose();
        }
    }

    private async Task<string?> TryGetPrecedingMessageJumpUrlAsync(
        IMessageChannel sourceChannel, ulong deletedMessageId, CancellationToken ct)
    {
        using var lookupTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookupTimeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var request = new RequestOptions { CancelToken = lookupTimeout.Token };

            IEnumerable<IMessage> messages = await sourceChannel
                .GetMessagesAsync(deletedMessageId, Direction.Before, limit: 1, options: request)
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
            _logger.Debug("Preceding message lookup timed out for deleted message {MessageId}", deletedMessageId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to fetch preceding message for deleted message {MessageId}", deletedMessageId);
            return null;
        }
    }

    private static bool IsQualifyingUserMessage(IMessage message)
    {
        return message.Source == MessageSource.User && !message.Author.IsBot && !message.Author.IsWebhook;
    }

    private static string BuildBulkDeleteText(
        ulong channelId,
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages)
    {
        List<string> details = [];
        var unknownCount = 0;
        var detailLength = 0;

        foreach (Cacheable<IMessage, ulong> cachedMessage in messages)
        {
            var deletedMessage = cachedMessage.HasValue ? cachedMessage.Value : null;

            if (deletedMessage is null)
                unknownCount++;

            var author = deletedMessage is null ? "author unknown" : $"author <@{deletedMessage.Author.Id}> (`{deletedMessage.Author.Id}`)";
            var content = deletedMessage is null ? "content unavailable" : $"content: {SingleLineSnippet(deletedMessage.Content)}";
            var line = $"- `{cachedMessage.Id}` {author}. {content}";
            var detailFits = details.Count < MaxBulkDetailCount
                             && detailLength + line.Length + 1 <= MaxBulkDetailLength;

            if (!detailFits)
                continue;

            details.Add(line);
            detailLength += line.Length + 1;
        }

        var omittedDetailCount = messages.Count - details.Count;

        if (omittedDetailCount > 0)
            details.Add($"- {omittedDetailCount} more message(s); summary truncated.");

        return $"Bulk delete in <#{channelId}> at {ModerationLogComponents.FormatTimestamp(DateTimeOffset.UtcNow)}. "
               + $"Messages: {messages.Count}. Cached: {messages.Count - unknownCount}. Unknown: {unknownCount}.\n"
               + string.Join('\n', details);
    }

    private static string SingleLineSnippet(string value)
    {
        var sanitized = value.ReplaceLineEndings(" ").Trim();

        if (sanitized.Length > 140)
        {
            var prefixLength = char.IsHighSurrogate(sanitized[136]) ? 136 : 137;
            sanitized = $"{sanitized.AsSpan(0, prefixLength)}...";
        }

        return $"`{sanitized.Replace('`', '\'')}`";
    }

}
