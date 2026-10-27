using Discord.Rest;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Gateway;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.Logging;

using DownloadedAttachment = ModerationLogComponents.DownloadedAttachment;

public sealed class ModerationLoggingGatewayHandler(
    DiscordSocketClient discordClient,
    IDbContextFactory<BotDbContext> dbContextFactory,
    HttpClient httpClient,
    IOptions<LoggingOptions> options,
    ILogger logger) : IDiscordGatewayHandler, IDisposable
{
    private const MessageFlags CompactLogMessageFlags   = MessageFlags.ComponentsV2 | MessageFlags.SuppressNotification;
    private const int          MaxUploadAttachmentCount = 10;
    private const long         MaxUploadAttachmentBytes = 8 * 1024 * 1024;

    private readonly ILogger                 _logger   = logger.ForContext<ModerationLoggingGatewayHandler>();
    private readonly LoggingOptions          _options  = options.Value;
    private readonly CancellationTokenSource _shutdown = new();
    private          CancellationToken       _shutdownToken;

    public Task InitializeAsync(CancellationToken ct)
    {
        _shutdownToken                    =  _shutdown.Token;
        discordClient.MessageUpdated      += DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted      += DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted += DispatchMessagesBulkDeletedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.MessageUpdated      -= DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted      -= DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted -= DispatchMessagesBulkDeletedAsync;
        _shutdown.Cancel();
    }

    public void Dispose()
    {
        Unsubscribe();
        _shutdown.Dispose();
    }

    private static async Task SendLogAsync(
        IMessageChannel logChannel,
        IReadOnlyCollection<DownloadedAttachment> attachments,
        CancellationToken ct,
        string? text = null,
        MessageComponent? components = null)
    {
        ct.ThrowIfCancellationRequested();
        var flags   = components is null ? MessageFlags.SuppressNotification : CompactLogMessageFlags;
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

    private static bool IsQualifyingUserMessage(IMessage message) => message.Source == MessageSource.User && !message.Author.IsBot && !message.Author.IsWebhook;

    private static async Task<ulong?> FindBanModeratorAsync(
        SocketGuild guild,
        ulong authorId,
        DateTimeOffset deletedAt,
        RequestOptions request)
    {
        IEnumerable<RestAuditLogEntry> entries = await guild
           .GetAuditLogsAsync(limit: 20, options: request, actionType: ActionType.Ban)
           .FlattenAsync();

        RestAuditLogEntry[] matchingEntries = entries
           .Where(entry => Math.Abs((entry.CreatedAt - deletedAt).TotalSeconds) <= 10)
           .Where(entry => entry.Data is BanAuditLogData data && data.Target?.Id == authorId)
           .ToArray();

        return matchingEntries.Length == 1 ? matchingEntries[0].User?.Id : null;
    }

    private Task DispatchMessageUpdatedAsync(
        Cacheable<IMessage, ulong> cachedMessage,
        SocketMessage updatedMessage,
        ISocketMessageChannel sourceChannel)
    {
        try
        {
            var isQualifyingUpdate = updatedMessage is SocketUserMessage && IsQualifyingUserMessage(updatedMessage);

            if (!isQualifyingUpdate || sourceChannel is not IGuildChannel guildChannel)
                return Task.CompletedTask;

            var beforeContent  = cachedMessage.HasValue ? cachedMessage.Value.Content : null;
            var afterContent   = updatedMessage.Content;
            var authorId       = updatedMessage.Author.Id;
            var messageId      = updatedMessage.Id;
            var guildId        = guildChannel.GuildId;
            var messageJumpUrl = updatedMessage.GetJumpUrl();
            var ct             = _shutdownToken;

            var contentIsUnchanged = cachedMessage.HasValue && string.Equals(a: beforeContent, b: afterContent, comparisonType: StringComparison.Ordinal);

            if (contentIsUnchanged)
                return Task.CompletedTask;

            _ = Task.Run(
                () => HandleMessageUpdatedAsync(
                    guildId: guildId,
                    sourceChannel: sourceChannel,
                    messageId: messageId,
                    authorId: authorId,
                    messageJumpUrl: messageJumpUrl,
                    beforeContent: beforeContent,
                    afterContent: afterContent,
                    ct: ct),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.Warning(exception: ex, "Failed to dispatch message edit {MessageId} in channel {ChannelId}", propertyValue0: updatedMessage.Id, propertyValue1: sourceChannel.Id);
        }

        return Task.CompletedTask;
    }

    private Task DispatchMessageDeletedAsync(
        Cacheable<IMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        try
        {
            if (!cachedMessage.HasValue)
                return Task.CompletedTask;

            var deletedMessage = cachedMessage.Value;

            if (!IsQualifyingUserMessage(deletedMessage))
                return Task.CompletedTask;

            var sourceChannel = cachedChannel.HasValue
                ? cachedChannel.Value
                : discordClient.GetChannel(cachedChannel.Id) as IMessageChannel;

            if (sourceChannel is not IGuildChannel guildChannel)
            {
                _logger.Debug(
                    "Skipping moderation log for deleted message {MessageId}: guild channel {ChannelId} is unavailable",
                    propertyValue0: cachedMessage.Id,
                    propertyValue1: cachedChannel.Id);

                return Task.CompletedTask;
            }

            var guildId = guildChannel.GuildId;
            var ct      = _shutdownToken;

            _ = Task.Run(
                () => HandleMessageDeletedAsync(
                    guildId: guildId,
                    sourceChannel: sourceChannel,
                    deletedMessage: deletedMessage,
                    ct: ct),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                exception: ex,
                "Failed to dispatch message deletion {MessageId} in channel {ChannelId}",
                propertyValue0: cachedMessage.Id,
                propertyValue1: cachedChannel.Id);
        }

        return Task.CompletedTask;
    }

    private Task DispatchMessagesBulkDeletedAsync(
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        try
        {
            var deletedAt = DateTimeOffset.UtcNow;

            IMessage[] cachedMessages = messages
               .Where(message => message.HasValue)
               .Select(message => message.Value)
               .ToArray();

            IMessage[] deletedMessages = cachedMessages
               .Where(IsQualifyingUserMessage)
               .ToArray();

            if (deletedMessages.Length == 0)
                return Task.CompletedTask;

            var authorIds = cachedMessages
               .Select(message => message.Author.Id)
               .Distinct()
               .Take(2)
               .ToArray();

            ulong? soleAuthorId = authorIds.Length == 1 ? authorIds[0] : null;
            var    ct           = _shutdownToken;

            _ = Task.Run(
                () => HandleMessagesBulkDeletedAsync(
                    messages: deletedMessages,
                    cachedChannel: cachedChannel,
                    originalMessageCount: messages.Count,
                    soleAuthorId: soleAuthorId,
                    deletedAt: deletedAt,
                    ct: ct),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.Warning(exception: ex, "Failed to dispatch bulk message deletion in channel {ChannelId}", propertyValue: cachedChannel.Id);
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
            var logChannel = await GetLogChannelAsync(guildId: guildId, sourceChannel: sourceChannel, forEdits: true, ct: ct);

            if (logChannel is null)
                return;

            var compactLog = ModerationLogComponents.BuildEditLog(
                messageJumpUrl: messageJumpUrl,
                channelId: sourceChannel.Id,
                authorId: authorId,
                beforeContent: beforeContent,
                afterContent: afterContent);

            await SendLogAsync(logChannel: logChannel, attachments: compactLog.Attachments, ct: ct, components: compactLog.Components);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Debug("Cancelled moderation log for edited message {MessageId} during shutdown", propertyValue: messageId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                exception: ex,
                "Failed to log message edit for {MessageId} in channel {ChannelId}",
                propertyValue0: messageId,
                propertyValue1: sourceChannel.Id);
        }
    }

    private async Task HandleMessageDeletedAsync(
        ulong guildId,
        IMessageChannel sourceChannel,
        IMessage deletedMessage,
        CancellationToken ct)
    {
        var messageId = deletedMessage.Id;

        try
        {
            ct.ThrowIfCancellationRequested();
            var logChannel = await GetLogChannelAsync(guildId: guildId, sourceChannel: sourceChannel, forEdits: false, ct: ct);

            if (logChannel is null)
                return;

            List<DownloadedAttachment> attachments             = [];
            var                        precedingMessageJumpUrl = await TryGetPrecedingMessageJumpUrlAsync(sourceChannel: sourceChannel, deletedMessageId: messageId, ct: ct);
            int                        omittedAttachmentCount;

            using (var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                downloadTimeout.CancelAfter(TimeSpan.FromSeconds(10));

                omittedAttachmentCount = await DownloadAttachmentsAsync(
                    deletedMessage: deletedMessage,
                    attachments: attachments,
                    ct: downloadTimeout.Token);
            }

            var compactLog = ModerationLogComponents.BuildDeleteLog(
                channelId: sourceChannel.Id,
                authorId: deletedMessage.Author.Id,
                precedingMessageJumpUrl: precedingMessageJumpUrl,
                content: deletedMessage.Content,
                attachments: attachments,
                omittedAttachmentCount: omittedAttachmentCount);

            await SendLogAsync(logChannel: logChannel, attachments: compactLog.Attachments, ct: ct, components: compactLog.Components);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Debug("Cancelled moderation log for deleted message {MessageId} during shutdown", propertyValue: messageId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                exception: ex,
                "Failed to log message deletion for {MessageId} in channel {ChannelId}",
                propertyValue0: messageId,
                propertyValue1: sourceChannel.Id);
        }
    }

    private async Task HandleMessagesBulkDeletedAsync(
        IReadOnlyCollection<IMessage> messages,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        int originalMessageCount,
        ulong? soleAuthorId,
        DateTimeOffset deletedAt,
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
                    propertyValue0: messages.Count,
                    propertyValue1: cachedChannel.Id);

                return;
            }

            var logChannel = await GetLogChannelAsync(guildId: guildChannel.GuildId, sourceChannel: sourceChannel, forEdits: false, ct: ct);

            if (logChannel is null)
                return;

            IEnumerable<(ulong AuthorId, ulong ChannelId, string Content)> rows = messages
               .Select(message => (message.Author.Id, message.Channel.Id, message.Content));

            var transcript = new DownloadedAttachment("bulk-delete.jsonl", "application/x-ndjson", BulkDeleteTranscript.Create(rows));

            var moderatorId = await TryGetBulkDeleteModeratorAsync(
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

            await SendLogAsync(logChannel: logChannel, [transcript], ct: ct, text: text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Debug(
                "Cancelled moderation log for bulk deletion in channel {ChannelId} during shutdown",
                propertyValue: cachedChannel.Id);
        }
        catch (Exception ex)
        {
            _logger.Warning(exception: ex, "Failed to log bulk message deletion in channel {ChannelId}", propertyValue: cachedChannel.Id);
        }
    }

    private async Task<ulong?> TryGetBulkDeleteModeratorAsync(
        ulong guildId,
        ulong channelId,
        int originalMessageCount,
        ulong? soleAuthorId,
        DateTimeOffset deletedAt,
        CancellationToken ct)
    {
        var guild = discordClient.GetGuild(guildId);

        if (guild?.CurrentUser.GuildPermissions.ViewAuditLog != true)
            return null;

        using var lookupTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookupTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var request = new RequestOptions { CancelToken = lookupTimeout.Token };

            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken: lookupTimeout.Token);

                IEnumerable<RestAuditLogEntry> bulkEntries = await guild
                   .GetAuditLogsAsync(limit: 20, options: request, actionType: ActionType.MessageBulkDeleted)
                   .FlattenAsync();

                RestAuditLogEntry[] matchingEntries = bulkEntries
                   .Where(entry => Math.Abs((entry.CreatedAt - deletedAt).TotalSeconds) <= 10)
                   .Where(entry => entry.Data is MessageBulkDeleteAuditLogData data
                              && data.ChannelId == channelId
                              && data.MessageCount == originalMessageCount)
                   .ToArray();

                if (matchingEntries.Length == 1)
                    return matchingEntries[0].User?.Id;

                var canMatchBan = attempt == 2 && matchingEntries.Length == 0;

                if (!canMatchBan || soleAuthorId is not { } authorId)
                    continue;

                // Give the direct deletion entry time to arrive before considering a ban.
                return await FindBanModeratorAsync(guild: guild, authorId: authorId, deletedAt: deletedAt, request: request);
            }

            _logger.Debug("No unique audit match for bulk deletion in guild {GuildId}, channel {ChannelId}", propertyValue0: guildId, propertyValue1: channelId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (lookupTimeout.IsCancellationRequested)
        {
            _logger.Debug("Audit lookup timed out for bulk deletion in guild {GuildId}, channel {ChannelId}", propertyValue0: guildId, propertyValue1: channelId);
        }
        catch (Exception ex)
        {
            _logger.Warning(exception: ex, "Failed to read audit log for bulk deletion in guild {GuildId}, channel {ChannelId}", propertyValue0: guildId, propertyValue1: channelId);
        }

        return null;
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

            var maxBytes       = Math.Min(val1: _options.MaxAttachmentBytesPerAttachment, val2: remainingBytes);
            var url            = string.IsNullOrWhiteSpace(attachment.Url) ? attachment.ProxyUrl : attachment.Url;
            var cannotDownload = attachment.Size > maxBytes || string.IsNullOrWhiteSpace(url);

            if (cannotDownload)
                continue;

            try
            {
                Option<byte[]> downloadResult = await AttachmentEvidenceDownloader.TryDownloadAsync(
                    httpClient: httpClient,
                    url: url,
                    maxBytes: maxBytes,
                    logger: _logger,
                    ct: ct);

                if (downloadResult.Value is Some<byte[]>(var bytes))
                {
                    var contentType = attachment.ContentType ?? "application/octet-stream";
                    var fileName    = ModerationLogComponents.GetAttachmentFileName(attachments.Count + 1, contentType: contentType);
                    attachments.Add(new DownloadedAttachment(FileName: fileName, ContentType: contentType, Bytes: bytes));
                    remainingBytes -= bytes.LongLength;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.Debug("Attachment download cancelled for deleted message {MessageId}", propertyValue: deletedMessage.Id);

                break;
            }
        }

        return deletedMessage.Attachments.Count - (attachments.Count - initialAttachmentCount);
    }

    private async Task<IMessageChannel?> GetLogChannelAsync(
        ulong guildId,
        IChannel sourceChannel,
        bool forEdits,
        CancellationToken ct)
    {
        List<ulong> scopeIds      = [sourceChannel.Id];
        var         parentChannel = sourceChannel;

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
               .SingleOrDefaultAsync(configuration => configuration.GuildId == guildId, cancellationToken: ct);

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
               .AnyAsync(excluded => excluded.GuildId == guildId && scopeIds.Contains(excluded.ChannelId), cancellationToken: ct);

            if (isExcluded)
                return null;
        }

        var logChannel     = discordClient.GetChannel(destinationChannelId.Value) as IMessageChannel;
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

    private async Task<string?> TryGetPrecedingMessageJumpUrlAsync(
        IMessageChannel sourceChannel,
        ulong deletedMessageId,
        CancellationToken ct)
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