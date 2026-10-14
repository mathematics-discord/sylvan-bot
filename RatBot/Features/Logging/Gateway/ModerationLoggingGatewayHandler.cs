using RatBot.Application.Features.Logging;
using RatBot.Domain.Features.Logging;
using RatBot.Gateway;
using RatBot.Infrastructure.Features.Logging;

namespace RatBot.Features.Logging.Gateway;

public sealed class ModerationLoggingGatewayHandler(
    DiscordSocketClient discordClient,
    MessageEvidenceCache evidenceCache,
    ModerationLoggingStore loggingStore,
    HttpClient httpClient,
    IOptions<LoggingOptions> options,
    ILogger logger
) : IDiscordGatewayHandler
{
    private const MessageFlags CompactLogMessageFlags = MessageFlags.ComponentsV2 | MessageFlags.SuppressNotification;

    private static readonly AllowedMentions NoMentions = AllowedMentions.None;

    private readonly ILogger _logger = logger.ForContext<ModerationLoggingGatewayHandler>();
    private readonly LoggingOptions _options = options.Value;

    public Task InitializeAsync(CancellationToken ct)
    {
        Subscribe();
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.MessageReceived -= HandleMessageReceivedAsync;
        discordClient.MessageUpdated -= HandleMessageUpdatedAsync;
        discordClient.MessageDeleted -= HandleMessageDeletedAsync;
        discordClient.MessagesBulkDeleted -= HandleMessagesBulkDeletedAsync;
    }

    private async Task HandleMessageReceivedAsync(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage || !IsQualifyingUserMessage(userMessage))
            return;

        if (!TryGetGuildId(userMessage.Channel, out ulong guildId))
            return;

        try
        {
            LoggingConfiguration configuration =
                await loggingStore.GetConfigurationAsync(guildId, CancellationToken.None).ConfigureAwait(false);

            if (!configuration.Enabled)
                return;

            bool isExcluded = await IsExcludedAsync(guildId, userMessage.Channel, CancellationToken.None)
                .ConfigureAwait(false);

            if (isExcluded)
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            await loggingStore
                .ObserveMessageAsync(
                    new ObservedMessage(userMessage.Id, guildId, userMessage.Channel.Id, userMessage.Author.Id, now),
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            MessageEvidence evidence = await CreateEvidenceAsync(guildId, userMessage, now, CancellationToken.None)
                .ConfigureAwait(false);
            evidenceCache.Put(evidence, now, configuration.EvidenceRetentionPeriod);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to observe message {MessageId} for moderation logging", userMessage.Id);
        }
    }

    private async Task HandleMessageUpdatedAsync(
        Cacheable<IMessage, ulong> cachedMessage,
        SocketMessage updatedMessage,
        ISocketMessageChannel channel
    )
    {
        if (updatedMessage is not SocketUserMessage userMessage || !IsQualifyingUserMessage(userMessage))
            return;

        if (!TryGetGuildId(channel, out ulong guildId))
            return;

        try
        {
            LoggingConfiguration configuration =
                await loggingStore.GetConfigurationAsync(guildId, CancellationToken.None).ConfigureAwait(false);

            if (!configuration.Enabled)
                return;

            bool isExcluded = await IsExcludedAsync(guildId, channel, CancellationToken.None).ConfigureAwait(false);

            if (isExcluded)
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool hasBefore = evidenceCache.TryGet(guildId, userMessage.Id, now, out MessageEvidence before);
            MessageEvidence after = await CreateEvidenceAsync(guildId, userMessage, now, CancellationToken.None)
                .ConfigureAwait(false);

            bool hasPreviousContent =
                TryGetPreviousContent(cachedMessage, before, hasBefore, out string? beforeContent);

            evidenceCache.Put(after, now, configuration.EvidenceRetentionPeriod);

            if (!hasPreviousContent || !string.Equals(beforeContent, after.Content, StringComparison.Ordinal))
            {
                ModerationLogComponents.ModerationLogMessage compactLog = ModerationLogComponents.BuildEditLog(
                    ModerationLogComponents.GetJumpUrl(userMessage),
                    channel.Id,
                    userMessage.Author.Id,
                    beforeContent,
                    after.Content
                );

                Option<IUserMessage> logResult =
                    await SendCompactLogAsync(configuration, LoggingEventKind.Edit, compactLog)
                        .ConfigureAwait(false);

                if (logResult.Value is Some<IUserMessage>(var logMessage))
                    await loggingStore
                        .RecordLogEntriesAsync(
                            new[] { new MessageLogEntry(guildId, userMessage.Id, logMessage.Id, now) },
                            CancellationToken.None)
                        .ConfigureAwait(false);
            }

            await loggingStore
                .ObserveMessageAsync(
                    new ObservedMessage(userMessage.Id, guildId, channel.Id, userMessage.Author.Id, now),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = cachedMessage;
            _logger.Warning(ex, "Failed to log message edit for {MessageId}", updatedMessage.Id);
        }
    }

    private async Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel)
    {
        IMessageChannel? channelValue = channel.HasValue ? channel.Value : null;

        if (channelValue is null || !TryGetGuildId(channelValue, out ulong guildId))
            return;

        try
        {
            LoggingConfiguration configuration =
                await loggingStore.GetConfigurationAsync(guildId, CancellationToken.None).ConfigureAwait(false);

            if (!configuration.Enabled)
                return;

            bool isExcluded =
                await IsExcludedAsync(guildId, channelValue, CancellationToken.None).ConfigureAwait(false);

            if (isExcluded)
                return;

            if (message.HasValue && !IsQualifyingUserMessage(message.Value))
            {
                evidenceCache.Remove(guildId, message.Id);
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            Option<ObservedMessage> observed = await loggingStore
                .FindObservedMessageAsync(guildId, message.Id, CancellationToken.None)
                .ConfigureAwait(false);
            bool hasEvidence = evidenceCache.TryGet(guildId, message.Id, now, out MessageEvidence evidence) &&
                               evidence.AuthorId != 0;

            if (observed.Value is None && !hasEvidence)
            {
                evidenceCache.Remove(guildId, message.Id);
                return;
            }

            ulong authorId = observed.Value is Some<ObservedMessage>(var observedMessage)
                ? observedMessage.AuthorId
                : evidence.AuthorId;

            string? content = (hasEvidence ? evidence.Content : null)
                ?? (message.HasValue ? message.Value.Content : null);

            Option<string> precedingMessageResult =
                await TryGetPrecedingMessageJumpUrlAsync(channelValue, message.Id)
                    .ConfigureAwait(false);
            string? precedingMessageJumpUrl = precedingMessageResult.Value is Some<string>(var jumpUrl) ? jumpUrl : null;
            try
            {
                ModerationLogComponents.ModerationLogMessage compactLog = ModerationLogComponents.BuildDeleteLog(
                    channelValue.Id,
                    authorId,
                    precedingMessageJumpUrl,
                    content,
                    hasEvidence ? evidence.Attachments : ImmutableArray<CachedAttachmentEvidence>.Empty
                );

                Option<IUserMessage> logResult = await SendDeleteLogAsync(
                        configuration,
                        message.Id,
                        channelValue.Id,
                        authorId,
                        precedingMessageJumpUrl,
                        content,
                        compactLog
                    )
                    .ConfigureAwait(false);

                if (logResult.Value is Some<IUserMessage>(var logMessage))
                    await loggingStore
                        .RecordLogEntriesAsync(new[] { new MessageLogEntry(guildId, message.Id, logMessage.Id, now) },
                            CancellationToken.None)
                        .ConfigureAwait(false);
            }
            finally
            {
                evidenceCache.Remove(guildId, message.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to log message deletion for {MessageId}", message.Id);
        }
    }

    private async Task HandleMessagesBulkDeletedAsync(
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> channel
    )
    {
        IMessageChannel? channelValue = channel.HasValue ? channel.Value : null;

        if (channelValue is null || !TryGetGuildId(channelValue, out ulong guildId))
            return;

        try
        {
            LoggingConfiguration configuration =
                await loggingStore.GetConfigurationAsync(guildId, CancellationToken.None).ConfigureAwait(false);

            if (!configuration.Enabled)
                return;

            bool isExcluded =
                await IsExcludedAsync(guildId, channelValue, CancellationToken.None).ConfigureAwait(false);

            if (isExcluded)
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ImmutableArray<ulong> messageIds = messages.Select(message => message.Id).Distinct().ToImmutableArray();
            IReadOnlyDictionary<ulong, ObservedMessage> observed = await loggingStore
                .FindObservedMessagesAsync(guildId, messageIds, CancellationToken.None)
                .ConfigureAwait(false);

            IReadOnlyDictionary<ulong, MessageEvidence> evidence = evidenceCache.GetMany(guildId, messageIds, now);
            ImmutableArray<ulong> qualifyingMessageIds =
                GetQualifyingBulkDeletedMessageIds(messageIds, messages, observed, evidence);

            if (qualifyingMessageIds.Length == 0)
            {
                RemoveEvidence(guildId, messageIds);
                return;
            }

            HashSet<ulong> qualifyingMessageIdSet = qualifyingMessageIds.ToHashSet();
            Dictionary<ulong, ObservedMessage> qualifyingObserved =
                FilterObservedMessages(observed, qualifyingMessageIdSet);
            Dictionary<ulong, MessageEvidence> qualifyingEvidence = FilterEvidence(evidence, qualifyingMessageIdSet);

            Option<IUserMessage> logResult = await SendLogAsync(
                    configuration,
                    LoggingEventKind.BulkDelete,
                    BuildBulkDeleteText(channelValue.Id, qualifyingMessageIds, qualifyingObserved, qualifyingEvidence,
                        now),
                    FlattenAttachments(qualifyingEvidence.Values)
                )
                .ConfigureAwait(false);

            if (logResult.Value is Some<IUserMessage>(var logMessage))
            {
                MessageLogEntry[] entries = qualifyingMessageIds
                    .Select(messageId => new MessageLogEntry(guildId, messageId, logMessage.Id, now))
                    .ToArray();

                await loggingStore.RecordLogEntriesAsync(entries, CancellationToken.None).ConfigureAwait(false);
            }

            RemoveEvidence(guildId, messageIds);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to log bulk message deletion in channel {ChannelId}", channelValue.Id);
        }
    }

    private async Task<MessageEvidence> CreateEvidenceAsync(ulong guildId, SocketUserMessage message,
        DateTimeOffset now, CancellationToken ct)
    {
        ImmutableArray<CachedAttachmentEvidence>.Builder attachments =
            ImmutableArray.CreateBuilder<CachedAttachmentEvidence>();

        foreach (Attachment attachment in message.Attachments.Take(_options.MaxAttachmentCountPerMessage))
        {
            Option<byte[]> downloadResult = await TryDownloadAttachmentAsync(attachment.Url, ct)
                .ConfigureAwait(false);

            if (downloadResult.Value is Some<byte[]>(var bytes))
                attachments.Add(new CachedAttachmentEvidence(attachments.Count + 1, bytes,
                    attachment.ContentType ?? "application/octet-stream"));
        }

        return new MessageEvidence(
            guildId,
            message.Channel.Id,
            message.Id,
            message.Author.Id,
            now,
            ToOptionalContent(message.Content).Value is Some<string>(var content) ? content : null,
            attachments.ToImmutable()
        );
    }

    private Task<Option<byte[]>> TryDownloadAttachmentAsync(string url, CancellationToken ct)
    {
        return AttachmentEvidenceDownloader.TryDownloadAsync(httpClient, url, _options.MaxAttachmentBytesPerAttachment,
            _logger, ct);
    }

    private async Task<bool> IsExcludedAsync(ulong guildId, IChannel channel, CancellationToken ct)
    {
        ImmutableArray<ulong> scopeIds = GetLoggingScopeIds(channel);

        return await loggingStore.IsAnyExcludedAsync(guildId, scopeIds, ct).ConfigureAwait(false);
    }

    private void RemoveEvidence(ulong guildId, IEnumerable<ulong> messageIds)
    {
        foreach (ulong messageId in messageIds)
            evidenceCache.Remove(guildId, messageId);
    }

    private async Task<Option<IUserMessage>> SendLogAsync(
        LoggingConfiguration configuration,
        LoggingEventKind eventKind,
        string text,
        IReadOnlyCollection<CachedAttachmentEvidence> attachments
    )
    {
        Option<ulong> destination = configuration.GetDestinationChannelId(eventKind);

        if (destination.Value is not Some<ulong>(var destinationChannelId) ||
            discordClient.GetChannel(destinationChannelId) is not IMessageChannel logChannel)
            return new None();

        if (attachments.Count == 0)
            return new Some<IUserMessage>(await logChannel.SendMessageAsync(text, allowedMentions: NoMentions)
                .ConfigureAwait(false));

        Queue<FileAttachment> files = new Queue<FileAttachment>();
        Queue<MemoryStream> streams = new Queue<MemoryStream>();

        try
        {
            foreach (CachedAttachmentEvidence attachment in attachments)
            {
                MemoryStream stream = new MemoryStream(attachment.Bytes);
                streams.Enqueue(stream);
                files.Enqueue(new FileAttachment(stream, $"evidence-{attachment.Index}.bin"));
            }

            return new Some<IUserMessage>(await logChannel.SendFilesAsync(files, text, allowedMentions: NoMentions)
                .ConfigureAwait(false));
        }
        finally
        {
            foreach (MemoryStream stream in streams)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Option<IUserMessage>> SendCompactLogAsync(
        LoggingConfiguration configuration,
        LoggingEventKind eventKind,
        ModerationLogComponents.ModerationLogMessage message
    )
    {
        Option<ulong> destination = configuration.GetDestinationChannelId(eventKind);

        if (destination.Value is not Some<ulong>(var destinationChannelId) ||
            discordClient.GetChannel(destinationChannelId) is not IMessageChannel logChannel)
            return new None();

        return new Some<IUserMessage>(await SendCompactLogAsync(logChannel, message)
            .ConfigureAwait(false));
    }

    private async Task<Option<IUserMessage>> SendDeleteLogAsync(
        LoggingConfiguration configuration,
        ulong messageId,
        ulong channelId,
        ulong authorId,
        string? precedingMessageJumpUrl,
        string? content,
        ModerationLogComponents.ModerationLogMessage message
    )
    {
        Option<ulong> destination = configuration.GetDestinationChannelId(LoggingEventKind.Delete);

        if (destination.Value is not Some<ulong>(var destinationChannelId) ||
            discordClient.GetChannel(destinationChannelId) is not IMessageChannel logChannel)
            return new None();

        IUserMessage sentMessage = await SendDeleteLogAsync(
                logChannel, messageId, channelId, authorId, precedingMessageJumpUrl, content, message, _logger)
            .ConfigureAwait(false);

        return new Some<IUserMessage>(sentMessage);
    }

    internal static async Task<IUserMessage> SendDeleteLogAsync(
        IMessageChannel logChannel,
        ulong messageId,
        ulong channelId,
        ulong authorId,
        string? precedingMessageJumpUrl,
        string? content,
        ModerationLogComponents.ModerationLogMessage message,
        ILogger logger
    )
    {
        if (message.Attachments.Length == 0)
            return await SendCompactLogAsync(logChannel, message).ConfigureAwait(false);

        try
        {
            return await SendCompactLogAsync(logChannel, message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Warning(
                ex,
                "Failed to send attachment evidence for delete log for message {MessageId} in channel {ChannelId}. Retrying without attachment evidence",
                messageId,
                channelId
            );

            ModerationLogComponents.ModerationLogMessage fallback = ModerationLogComponents.BuildDeleteLog(
                channelId,
                authorId,
                precedingMessageJumpUrl,
                content,
                ImmutableArray<CachedAttachmentEvidence>.Empty
            );

            return await SendCompactLogAsync(logChannel, fallback).ConfigureAwait(false);
        }
    }

    internal static async Task<IUserMessage> SendCompactLogAsync(IMessageChannel logChannel,
        ModerationLogComponents.ModerationLogMessage message)
    {
        if (message.Attachments.Length == 0)
            return await logChannel
                .SendMessageAsync(components: message.Components, allowedMentions: NoMentions,
                    flags: CompactLogMessageFlags)
                .ConfigureAwait(false);

        Queue<FileAttachment> files = new Queue<FileAttachment>();
        Queue<MemoryStream> streams = new Queue<MemoryStream>();

        try
        {
            foreach (ModerationLogComponents.ModerationLogAttachment attachment in message.Attachments)
            {
                MemoryStream stream = new MemoryStream(attachment.Bytes);
                streams.Enqueue(stream);
                files.Enqueue(new FileAttachment(stream, attachment.FileName));
            }

            return await logChannel
                .SendFilesAsync(files, components: message.Components, allowedMentions: NoMentions,
                    flags: CompactLogMessageFlags)
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (MemoryStream stream in streams)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string BuildBulkDeleteText(
        ulong channelId,
        ImmutableArray<ulong> messageIds,
        IReadOnlyDictionary<ulong, ObservedMessage> observed,
        IReadOnlyDictionary<ulong, MessageEvidence> evidence,
        DateTimeOffset deletedAtUtc
    )
    {
        int attributedCount = messageIds.Count(observed.ContainsKey);
        int evidenceCount = messageIds.Count(evidence.ContainsKey);
        string details = BuildBulkDeleteDetails(messageIds, observed, evidence);

        return
            $"Bulk delete in <#{channelId}> at {ModerationLogComponents.FormatTimestamp(deletedAtUtc)}. Messages: {messageIds.Length}. "
            + $"Attributed: {attributedCount}. Cached evidence: {evidenceCount}.\n"
            + details;
    }

    private static string BuildBulkDeleteDetails(
        ImmutableArray<ulong> messageIds,
        IReadOnlyDictionary<ulong, ObservedMessage> observed,
        IReadOnlyDictionary<ulong, MessageEvidence> evidence
    )
    {
        List<string> lines = new List<string>();

        foreach (ulong messageId in messageIds.Take(20))
        {
            string author = observed.TryGetValue(messageId, out ObservedMessage? observedMessage)
                ? $"author <@{observedMessage.AuthorId}> (`{observedMessage.AuthorId}`)"
                : "author unknown";
            string content =
                evidence.TryGetValue(messageId, out MessageEvidence? messageEvidence) &&
                messageEvidence.Content is not null
                    ? $" content: {SingleLineSnippet(messageEvidence.Content)}"
                    : string.Empty;

            lines.Add($"- `{messageId}` {author}.{content}");
        }

        if (messageIds.Length > 20)
            lines.Add($"- and {messageIds.Length - 20} more message(s).");

        return string.Join('\n', lines);
    }

    private static ImmutableArray<CachedAttachmentEvidence> FlattenAttachments(IEnumerable<MessageEvidence> evidence)
    {
        return evidence.SelectMany(item => item.Attachments).Take(10).ToImmutableArray();
    }

    private static ImmutableArray<ulong> GetLoggingScopeIds(IChannel channel)
    {
        ImmutableArray<ulong>.Builder ids = ImmutableArray.CreateBuilder<ulong>();
        ids.Add(channel.Id);

        if (channel is SocketThreadChannel threadChannel)
        {
            AddParentScopeIds(ids, threadChannel.ParentChannel);
            return ids.ToImmutable();
        }

        AddCategoryScopeId(ids, channel);
        return ids.ToImmutable();
    }

    private static void AddParentScopeIds(ImmutableArray<ulong>.Builder ids, IChannel parentChannel)
    {
        ids.Add(parentChannel.Id);
        AddCategoryScopeId(ids, parentChannel);
    }

    private static void AddCategoryScopeId(ImmutableArray<ulong>.Builder ids, IChannel channel)
    {
        if (channel is INestedChannel { CategoryId: { } categoryId })
            ids.Add(categoryId);
    }

    private static bool HasQualifyingEvidence(ulong messageId, IReadOnlyDictionary<ulong, MessageEvidence> evidence)
    {
        return evidence.TryGetValue(messageId, out MessageEvidence? messageEvidence) && messageEvidence.AuthorId != 0;
    }

    private static bool IsQualifyingBulkDeletedMessage(
        ulong messageId,
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        IReadOnlyDictionary<ulong, ObservedMessage> observed,
        IReadOnlyDictionary<ulong, MessageEvidence> evidence
    )
    {
        Cacheable<IMessage, ulong> cachedMessage = messages.First(message => message.Id == messageId);

        if (cachedMessage.HasValue && !IsQualifyingUserMessage(cachedMessage.Value))
            return false;

        return observed.ContainsKey(messageId) || HasQualifyingEvidence(messageId, evidence);
    }

    private static ImmutableArray<ulong> GetQualifyingBulkDeletedMessageIds(
        ImmutableArray<ulong> messageIds,
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        IReadOnlyDictionary<ulong, ObservedMessage> observed,
        IReadOnlyDictionary<ulong, MessageEvidence> evidence
    )
    {
        return messageIds.Where(messageId => IsQualifyingBulkDeletedMessage(messageId, messages, observed, evidence))
            .ToImmutableArray();
    }

    private static Dictionary<ulong, ObservedMessage> FilterObservedMessages(
        IReadOnlyDictionary<ulong, ObservedMessage> observed,
        HashSet<ulong> messageIds
    )
    {
        return observed.Where(pair => messageIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static Dictionary<ulong, MessageEvidence> FilterEvidence(
        IReadOnlyDictionary<ulong, MessageEvidence> evidence,
        HashSet<ulong> messageIds
    )
    {
        return evidence.Where(pair => messageIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static bool TryGetPreviousContent(Cacheable<IMessage, ulong> cachedMessage, MessageEvidence before,
        bool hasBefore, out string? content)
    {
        if (hasBefore)
        {
            content = before.Content;
            return true;
        }

        if (cachedMessage.HasValue)
        {
            content = ToOptionalContent(cachedMessage.Value.Content).Value is Some<string>(var value) ? value : null;
            return true;
        }

        content = null;
        return false;
    }

    private async Task<Option<string>> TryGetPrecedingMessageJumpUrlAsync(
        IMessageChannel channel, ulong deletedMessageId)
    {
        try
        {
            IEnumerable<IMessage> messages = await channel
                .GetMessagesAsync(deletedMessageId, Direction.Before, 1)
                .FlattenAsync()
                .ConfigureAwait(false);
            IMessage? precedingMessage = messages.FirstOrDefault();

            return precedingMessage is null
                ? new None()
                : new Some<string>(ModerationLogComponents.GetJumpUrl(precedingMessage));
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to fetch preceding message for deleted message {MessageId}", deletedMessageId);
            return new None();
        }
    }

    private static bool IsQualifyingUserMessage(IMessage message)
    {
        return message.Source == MessageSource.User && !message.Author.IsBot && !message.Author.IsWebhook;
    }

    private static Option<string> ToOptionalContent(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? new None() : new Some<string>(value);
    }

    private static string SingleLineSnippet(string value)
    {
        string sanitized = value.ReplaceLineEndings(" ").Trim();

        if (sanitized.Length > 140)
            sanitized = $"{sanitized.AsSpan(0, 137)}...";

        return $"`{sanitized.Replace('`', '\'')}`";
    }

    private static bool TryGetGuildId(IChannel channel, out ulong guildId)
    {
        switch (channel)
        {
            case IGuildChannel guildChannel:
                guildId = guildChannel.GuildId;
                return true;
            default:
                guildId = 0;
                return false;
        }
    }

    private Task DispatchMessageReceivedAsync(SocketMessage message)
    {
        _ = Task.Run(() => HandleMessageReceivedAsync(message));
        return Task.CompletedTask;
    }

    private void Subscribe()
    {
        discordClient.MessageReceived += DispatchMessageReceivedAsync;
        discordClient.MessageUpdated += HandleMessageUpdatedAsync;
        discordClient.MessageDeleted += HandleMessageDeletedAsync;
        discordClient.MessagesBulkDeleted += HandleMessagesBulkDeletedAsync;
    }
}