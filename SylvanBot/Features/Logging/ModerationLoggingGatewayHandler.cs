using System.Threading.Channels;
using Discord.Rest;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Features.MessageCache;
using SylvanBot.Gateway;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.Logging;

using DownloadedAttachment = ModerationLogComponents.DownloadedAttachment;

public sealed class ModerationLoggingGatewayHandler(
    DiscordSocketClient discordClient,
    IDbContextFactory<BotDbContext> dbContextFactory,
    HttpClient httpClient,
    IOptions<LoggingOptions> options,
    MessageSnapshotCache cache,
    MessageUpdateGatewayAdapter updatePayload,
    ILogger logger) : BackgroundService, IDiscordGatewayHandler
{
    private const MessageFlags CompactLogMessageFlags = MessageFlags.ComponentsV2 | MessageFlags.SuppressNotification;
    private const int MaxUploadAttachmentCount = 10;
    private const long MaxUploadAttachmentBytes = 8 * 1024 * 1024;

    private readonly ILogger _logger = logger.ForContext<ModerationLoggingGatewayHandler>();
    private readonly LoggingOptions _options = options.Value;

    private readonly Channel<CacheWork> _work = Channel.CreateBounded<CacheWork>(
        new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private CancellationToken _stoppingToken;
    private long _failures;

    public long Failures => Interlocked.Read(ref _failures);

    public Task InitializeAsync(CancellationToken ct)
    {
        discordClient.MessageReceived += DispatchMessageReceivedAsync;
        discordClient.MessageUpdated += DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted += DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted += DispatchMessagesBulkDeletedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.MessageReceived -= DispatchMessageReceivedAsync;
        discordClient.MessageUpdated -= DispatchMessageUpdatedAsync;
        discordClient.MessageDeleted -= DispatchMessageDeletedAsync;
        discordClient.MessagesBulkDeleted -= DispatchMessagesBulkDeletedAsync;
        _work.Writer.TryComplete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                try
                {
                    await ProcessAsync(work, timeout.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _failures);
                    _logger.Warning("Message handling timed out for guild {GuildId}, channel {ChannelId}", work.GuildId, work.ChannelId);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref _failures);

                    _logger.Warning(
                        "Message cache handling failed for guild {GuildId}, channel {ChannelId}, message {MessageId}: {ExceptionType}",
                        work.GuildId,
                        work.ChannelId,
                        work switch
                        {
                            ReceivedWork received => received.Message.MessageId,
                            EditedWork edited => edited.Message.MessageId,
                            _ => (ulong?)null,
                        },
                        exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Already persisted observations survive shutdown; pending events are transient and are not replayed.
        }
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

    private Task DispatchMessageReceivedAsync(SocketMessage message)
    {
        if (!IsQualifyingUserMessage(message) || message.Channel is not IGuildChannel guildChannel)
            return Task.CompletedTask;

        ImmutableArray<MessageAttachment> attachments = message.Attachments
            .Select(attachment => new MessageAttachment(attachment.Url, attachment.ProxyUrl, attachment.ContentType, attachment.Size))
            .ToImmutableArray();

        var valid = attachments.All(attachment => !string.IsNullOrWhiteSpace(attachment.Url) && attachment.Size >= 0);

        if (!valid || message.Content is not { } content)
        {
            Interlocked.Increment(ref _failures);
            _logger.Warning("Invalid received message metadata for {MessageId}; cache observation skipped", message.Id);

            return Task.CompletedTask;
        }

        var received = new MessageReceived
        {
            GuildId = guildChannel.GuildId,
            ChannelId = message.Channel.Id,
            MessageId = message.Id,
            AuthorId = message.Author.Id,
            CreatedAtUtc = SnowflakeUtils.FromSnowflake(message.Id),
            Eligible = true,
            Content = content,
            Attachments = attachments,
            EditedAtUtc = message.EditedTimestamp?.ToUniversalTime(),
        };

        return QueueAsync(new ReceivedWork(received));
    }

    private Task DispatchMessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (after is not SocketUserMessage || channel is not IGuildChannel guildChannel)
            return Task.CompletedTask;

        // Never infer absence from Discord.NET's detached defaults (notably the empty attachment array).
        Option<MessageEdited> observation = updatePayload.Current;

        if (observation.Value is not Some<MessageEdited>(var payload))
            return Task.CompletedTask;

        if (payload.MessageId != after.Id || payload.ChannelId != channel.Id || payload.GuildId != guildChannel.GuildId)
        {
            Interlocked.Increment(ref _failures);
            _logger.Warning("Message update field presence unavailable for {MessageId}; update skipped", after.Id);

            return Task.CompletedTask;
        }

        var edited = payload with
        {
            GuildId = guildChannel.GuildId,
            ChannelId = channel.Id,
            AuthorId = after.Author.Id == 0 ? new None() : new Some<ulong>(after.Author.Id),
            Eligible = after.Author.Id == 0 ? new None() : new Some<bool>(IsQualifyingUserMessage(after)),
        };

        return QueueAsync(new EditedWork(edited));
    }

    private Task DispatchMessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel) =>
        QueueDeleteAsync(channel.Id, [message.Id], bulk: false);

    private Task DispatchMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
    {
        ImmutableArray<ulong> ids = messages
            .Select(message => message.Id)
            .ToImmutableArray();

        return QueueDeleteAsync(channel.Id, ids, bulk: true);
    }

    private Task QueueDeleteAsync(ulong channelId, ImmutableArray<ulong> ids, bool bulk)
    {
        if (discordClient.GetChannel(channelId) is not IGuildChannel channel)
            return Task.CompletedTask;

        return QueueAsync(new DeletionWork(channel.GuildId, channelId, ids, DateTimeOffset.UtcNow, bulk));
    }

    private async Task QueueAsync(CacheWork work)
    {
        try
        {
            // Await admission only. Sustained overload applies bounded backpressure to gateway dispatch.
            await _work.Writer.WriteAsync(work, _stoppingToken);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
            // Shutdown has stopped admission.
        }
    }

    private async Task ProcessAsync(CacheWork work, CancellationToken ct)
    {
        if (discordClient.GetChannel(work.ChannelId) is not IMessageChannel sourceChannel
            || sourceChannel is not IGuildChannel guildChannel
            || guildChannel.GuildId != work.GuildId)
            return;

        if (work is DeletionWork deletion)
        {
            await ProcessDeletionAsync(deletion, sourceChannel, ct);

            return;
        }

        var configuration = await GetConfigurationAsync(work.GuildId, sourceChannel, ct);

        if (configuration is null)
            return;

        switch (work)
        {
            case ReceivedWork received:
                await cache.ReceiveAsync(received.Message, ct);

                break;
            case EditedWork edited:
                await ProcessEditAsync(edited.Message, sourceChannel, ct);

                break;
        }
    }

    private async Task ProcessEditAsync(MessageEdited edited, IMessageChannel sourceChannel, CancellationToken ct)
    {
        Option<MessageEdit> result = await cache.EditAsync(edited, ct);

        if (result.Value is not Some<MessageEdit>(var change))
            return;

        var after = change.After;
        var beforeContent = change.Before.Value is Some<CachedMessageSnapshot>(var before) ? before.Content : null;

        var contentChanged = edited.Content.Value is Some<string>
                             && !string.Equals(beforeContent, after.Content, StringComparison.Ordinal);

        if (contentChanged)
            await HandleMessageUpdatedAsync(
                edited.GuildId,
                sourceChannel,
                after.AuthorId,
                $"https://discord.com/channels/{edited.GuildId}/{edited.ChannelId}/{after.MessageId}",
                beforeContent,
                after.Content,
                after.ExpiresAtUtc,
                ct);
    }

    private async Task ProcessDeletionAsync(DeletionWork work, IMessageChannel sourceChannel, CancellationToken ct)
    {
        ImmutableArray<CachedMessageSnapshot> snapshots = await cache.BeginDeleteAsync(work.GuildId, work.DeletedMessageIds, ct);

        if (snapshots.IsEmpty)
            return;

        bool published;

        if (work.Bulk)
        {
            var authors = snapshots
                .Select(message => message.AuthorId)
                .Distinct()
                .Take(2)
                .ToArray();

            // Uncached IDs may belong to another author, including an excluded bot or webhook.
            var soleAuthorKnown = authors.Length == 1 && snapshots.Length == work.DeletedMessageIds.Length;

            published = await HandleMessagesBulkDeletedAsync(
                snapshots,
                sourceChannel,
                work.DeletedMessageIds.Length,
                soleAuthorKnown ? authors[0] : null,
                work.DeletedAt,
                ct);
        }
        else
            published = await HandleMessageDeletedAsync(work.GuildId, sourceChannel, snapshots[0], ct);

        if (published)
            await cache.CompleteDeleteAsync(snapshots, ct);
    }

    private async Task HandleMessageUpdatedAsync(
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
        var logChannel = await GetLogChannelAsync(guildId: guildId, sourceChannel: sourceChannel, forEdits: true, ct: ct);

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

    private async Task<bool> HandleMessageDeletedAsync(
        ulong guildId,
        IMessageChannel sourceChannel,
        CachedMessageSnapshot deletedMessage,
        CancellationToken ct)
    {
        var messageId = deletedMessage.MessageId;

        ct.ThrowIfCancellationRequested();
        var logChannel = await GetLogChannelAsync(guildId: guildId, sourceChannel: sourceChannel, forEdits: false, ct: ct);

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

    private async Task<bool> HandleMessagesBulkDeletedAsync(
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

        var logChannel = await GetLogChannelAsync(guildId: guildChannel.GuildId, sourceChannel: sourceChannel, forEdits: false, ct: ct);

        if (logChannel is null)
            return false;

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
        CachedMessageSnapshot deletedMessage,
        List<DownloadedAttachment> attachments,
        CancellationToken ct)
    {
        var initialAttachmentCount = attachments.Count;

        var uploadedBytes = attachments
            .Sum(attachment => attachment.Bytes.LongLength);

        var remainingBytes = MaxUploadAttachmentBytes - uploadedBytes;

        IEnumerable<MessageAttachment> candidates = deletedMessage.Attachments
            .Take(_options.MaxAttachmentCountPerMessage);

        foreach (var attachment in candidates)
        {
            var uploadLimitReached = attachments.Count >= MaxUploadAttachmentCount
                                     || remainingBytes <= 0
                                     || ct.IsCancellationRequested;

            if (uploadLimitReached)
                break;

            var maxBytes = Math.Min(val1: _options.MaxAttachmentBytesPerAttachment, val2: remainingBytes);
            var url = string.IsNullOrWhiteSpace(attachment.Url) ? attachment.ProxyUrl : attachment.Url;
            var cannotDownload = attachment.Size > maxBytes || string.IsNullOrWhiteSpace(url);

            if (cannotDownload || string.IsNullOrWhiteSpace(url))
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

    private async Task<IMessageChannel?> GetLogChannelAsync(
        ulong guildId,
        IChannel sourceChannel,
        bool forEdits,
        CancellationToken ct)
    {
        var configuration = await GetConfigurationAsync(guildId, sourceChannel, ct);
        var destinationChannelId = forEdits ? configuration?.EditLogChannelId : configuration?.DeleteLogChannelId;

        if (destinationChannelId is null)
            return null;

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

    private async Task<LoggingConfiguration?> GetConfigurationAsync(ulong guildId, IChannel sourceChannel, CancellationToken ct)
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

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var configuration = await db.LoggingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.GuildId == guildId, ct);

        var enabled = configuration is { Enabled: true }
                      && (configuration.EditLogChannelId is not null || configuration.DeleteLogChannelId is not null);

        if (!enabled || configuration is null)
            return null;

        var outputChannel = scopeIds.Contains(configuration.DeleteLogChannelId ?? 0) || scopeIds.Contains(configuration.EditLogChannelId ?? 0);

        var excluded = await db.LoggingExcludedChannels
            .AsNoTracking()
            .AnyAsync(channel => channel.GuildId == guildId && scopeIds.Contains(channel.ChannelId), ct);

        return outputChannel || excluded ? null : configuration;
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

    private abstract record CacheWork(ulong GuildId, ulong ChannelId);

    private sealed record ReceivedWork(MessageReceived Message) : CacheWork(Message.GuildId, Message.ChannelId);

    private sealed record EditedWork(MessageEdited Message) : CacheWork(Message.GuildId, Message.ChannelId);

    private sealed record DeletionWork(
        ulong GuildId,
        ulong ChannelId,
        ImmutableArray<ulong> DeletedMessageIds,
        DateTimeOffset DeletedAt,
        bool Bulk) : CacheWork(GuildId, ChannelId);
}