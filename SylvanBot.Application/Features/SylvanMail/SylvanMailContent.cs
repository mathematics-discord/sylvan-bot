using System.Collections.Immutable;

namespace SylvanBot.Application.Features.SylvanMail;

internal static class SylvanMailContent
{
    private const int MaxAttachmentBytes = 8 * 1024 * 1024;

    public static void Validate(SylvanMailMessage message)
    {
        var totalBytes = message.Attachments.Sum(item => (long)item.Size);

        var oversized = message.Attachments
           .Any(item => item.Size is < 0 or > MaxAttachmentBytes);

        if (message.UnsupportedContent)
        {
            throw new BotCommandException(
                "Not relayed: stickers, polls, voice messages, forwards and interactive content are unsupported. Send text or ordinary attachments.");
        }

        var exceedsLimits = message.Content.Length > 16000 || message.Attachments.Length > 10 || oversized || totalBytes > 24 * 1024 * 1024;

        if (exceedsLimits)
            throw new BotCommandException("Not relayed: maximum 16,000 text characters, 10 attachments, 8 MiB per file and 24 MiB total.");

        if (string.IsNullOrWhiteSpace(message.Content) && message.Attachments.IsEmpty)
            throw new BotCommandException("Not relayed: no supported text or attachments remain after removing Sylvan's mention.");
    }

    public static ImmutableArray<string> SplitContent(string content, int size)
    {
        var chunks = ImmutableArray.CreateBuilder<string>();
        var index = 0;

        while (index < content.Length)
        {
            var length = Math.Min(val1: size, content.Length - index);

            if (index + length < content.Length && char.IsHighSurrogate(content[(index + length) - 1]))
                length--;

            chunks.Add(content.Substring(startIndex: index, length: length));
            index += length;
        }

        return chunks.ToImmutable();
    }
}