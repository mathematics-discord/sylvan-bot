using System.Text.Json;
using Discord.Net.WebSockets;
using SylvanBot.Application.Features.MessageCache;

namespace SylvanBot.Features.MessageCache;

/// <summary>Preserves update field presence while Discord.NET dispatches its detached message.</summary>
public sealed class MessageUpdateGatewayAdapter(ILogger logger)
{
    private readonly AsyncLocal<MessageEdited?> _current = new AsyncLocal<MessageEdited?>();
    private readonly ILogger _logger = logger.ForContext<MessageUpdateGatewayAdapter>();

    public Option<MessageEdited> Current => _current.Value is { } update ? new Some<MessageEdited>(update) : new None();

    public IWebSocketClient CreateWebSocket() => new UpdateWebSocket(this, DefaultWebSocketProvider.Create()());

    private sealed class UpdateWebSocket : IWebSocketClient
    {
        private readonly MessageUpdateGatewayAdapter _owner;
        private readonly IWebSocketClient _inner;

        public UpdateWebSocket(MessageUpdateGatewayAdapter owner, IWebSocketClient inner)
        {
            _owner = owner;
            _inner = inner;
            _inner.TextMessage += ForwardTextAsync;
        }

        public event Func<string, Task>? TextMessage;

        public event Func<byte[], int, int, Task> BinaryMessage
        {
            add => _inner.BinaryMessage += value;
            remove => _inner.BinaryMessage -= value;
        }

        public event Func<Exception, Task> Closed
        {
            add => _inner.Closed += value;
            remove => _inner.Closed -= value;
        }

        // Discord's uncompressed JSON transport lets us preserve presence without a second zlib decoder.
        public Task ConnectAsync(string host) => _inner.ConnectAsync(host.Replace("&compress=zlib-stream", string.Empty, StringComparison.Ordinal));

        public Task DisconnectAsync(int closeCode = 1000) => _inner.DisconnectAsync(closeCode);

        public Task SendAsync(byte[] data, int index, int count, bool isText) => _inner.SendAsync(data, index, count, isText);

        public void SetHeader(string key, string value) => _inner.SetHeader(key, value);

        public void SetCancelToken(CancellationToken cancelToken) => _inner.SetCancelToken(cancelToken);

        public void Dispose()
        {
            _inner.TextMessage -= ForwardTextAsync;
            _inner.Dispose();
        }

        private async Task ForwardTextAsync(string text)
        {
            try
            {
                try
                {
                    using var document = JsonDocument.Parse(text);
                    var frame = document.RootElement;
                    var isUpdate =
                        frame.TryGetProperty("t", out var type) && string.Equals(type.GetString(), "MESSAGE_UPDATE", StringComparison.Ordinal);

                    if (isUpdate && frame.GetProperty("d").TryGetProperty("guild_id", out _))
                        _owner._current.Value = ReadUpdate(frame.GetProperty("d"));
                }
                catch (Exception exception)
                    when (exception is JsonException or FormatException or InvalidOperationException or OverflowException or KeyNotFoundException)
                {
                    _owner._logger.Warning(
                        "Message update extraction failed: {ExceptionType}; forwarding gateway frame without cache update",
                        exception.GetType().Name
                    );
                }

                if (TextMessage is { } handler)
                    await handler(text);
            }
            finally
            {
                _owner._current.Value = null;
            }
        }

        private static MessageEdited ReadUpdate(JsonElement data)
        {
            var messageId = ReadId(data.GetProperty("id"));
            var channelId = ReadId(data.GetProperty("channel_id"));
            var guildId = data.TryGetProperty("guild_id", out var guild) ? ReadId(guild) : 0;
            Option<DateTimeOffset?> editedAtUtc = new None();
            Option<string> content = new None();
            Option<ImmutableArray<MessageAttachment>> attachments = new None();

            if (data.TryGetProperty("edited_timestamp", out var editedAt))
            {
                DateTimeOffset? timestamp = editedAt.ValueKind == JsonValueKind.Null ? null : editedAt.GetDateTimeOffset().ToUniversalTime();
                editedAtUtc = new Some<DateTimeOffset?>(timestamp);
            }

            if (data.TryGetProperty("content", out var body))
                content = new Some<string>(body.GetString() ?? throw new JsonException("Content must be a string."));

            if (data.TryGetProperty("attachments", out var attachmentData))
            {
                ImmutableArray<MessageAttachment> suppliedAttachments = attachmentData
                    .EnumerateArray()
                    .Select(attachment => new MessageAttachment(
                        attachment.GetProperty("url").GetString() ?? throw new JsonException("Attachment URL must be a string."),
                        attachment.TryGetProperty("proxy_url", out var proxy) ? proxy.GetString() : null,
                        attachment.TryGetProperty("content_type", out var contentType) ? contentType.GetString() : null,
                        attachment.GetProperty("size").GetInt64()
                    ))
                    .ToImmutableArray();

                if (suppliedAttachments.Any(attachment => string.IsNullOrWhiteSpace(attachment.Url) || attachment.Size < 0))
                    throw new JsonException("Invalid attachment metadata.");

                attachments = new Some<ImmutableArray<MessageAttachment>>(suppliedAttachments);
            }

            return new MessageEdited
            {
                GuildId = guildId,
                ChannelId = channelId,
                MessageId = messageId,
                CreatedAtUtc = SnowflakeUtils.FromSnowflake(messageId),
                EditedAtUtc = editedAtUtc,
                Content = content,
                Attachments = attachments,
            };
        }

        private static ulong ReadId(JsonElement value) =>
            ulong.Parse(
                value.GetString() ?? throw new JsonException("Message identifier missing."),
                System.Globalization.CultureInfo.InvariantCulture
            );
    }
}
