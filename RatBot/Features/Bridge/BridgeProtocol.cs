namespace RatBot.Features.Bridge;

using System.Globalization;
using System.Text.Json;

public static class BridgeProtocol
{
    public const int Version = 1;
    public const int MaxFrameBytes = 65536;
    public const int MaxReplyCharacters = 16000;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static BridgeParseResult Parse(ReadOnlyMemory<byte> json)
    {
        string? requestId = null;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return new BridgeParseResult(null, null, "Expected a JSON object.");

            requestId = ReadString(root, "requestId");

            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64)
                return new BridgeParseResult(null, null, "requestId must contain 1 to 64 characters.");

            HashSet<string> names = new(StringComparer.Ordinal);

            foreach (var property in root.EnumerateObject())
            {
                var known = property.Name is "version" or "type" or "requestId" or "messageId" or "text";

                if (!known || !names.Add(property.Name))
                    return new BridgeParseResult(null, requestId, "Unknown or duplicate property.");
            }

            var validVersion =
                root.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var number)
                && number == Version;

            if (!validVersion)
                return new BridgeParseResult(null, requestId, "Unsupported protocol version; expected 1.");

            var type = ReadString(root, "type");

            if (type is not ("typing" or "reply"))
                return new BridgeParseResult(null, requestId, "type must be typing or reply.");

            var messageIdText = ReadString(root, "messageId");
            var validId =
                ulong.TryParse(messageIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var messageId)
                && messageId != 0
                && string.Equals(messageId.ToString(CultureInfo.InvariantCulture), messageIdText, StringComparison.Ordinal);

            if (!validId)
                return new BridgeParseResult(null, requestId, "messageId must be a non-zero decimal ID string.");

            var text = ReadString(root, "text");

            var isReply = string.Equals(type, "reply", StringComparison.Ordinal);

            if (isReply && (string.IsNullOrWhiteSpace(text) || text.Length > MaxReplyCharacters))
                return new BridgeParseResult(null, requestId, "reply text must contain 1 to 16000 characters.");

            if (!isReply && names.Contains("text"))
                return new BridgeParseResult(null, requestId, "typing does not accept text.");

            return new BridgeParseResult(new BridgeAction(type, requestId, messageId, text), requestId, null);
        }
        catch (JsonException)
        {
            return new BridgeParseResult(null, requestId, "Invalid JSON.");
        }
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

public sealed record BridgeAction
{
    public BridgeAction(string Type, string RequestId, ulong MessageId, string? Text)
    {
        this.Type = Type;
        this.RequestId = RequestId;
        this.MessageId = MessageId;
        this.Text = Text;
    }

    public string Type { get; init; }
    public string RequestId { get; init; }
    public ulong MessageId { get; init; }
    public string? Text { get; init; }

    public void Deconstruct(out string Type, out string RequestId, out ulong MessageId, out string? Text)
    {
        Type = this.Type;
        RequestId = this.RequestId;
        MessageId = this.MessageId;
        Text = this.Text;
    }
}

public sealed record BridgeParseResult
{
    public BridgeParseResult(BridgeAction? Action, string? RequestId, string? Error)
    {
        this.Action = Action;
        this.RequestId = RequestId;
        this.Error = Error;
    }

    public BridgeAction? Action { get; init; }
    public string? RequestId { get; init; }
    public string? Error { get; init; }

    public void Deconstruct(out BridgeAction? Action, out string? RequestId, out string? Error)
    {
        Action = this.Action;
        RequestId = this.RequestId;
        Error = this.Error;
    }
}
