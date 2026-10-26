namespace SylvanBot.Features.Logging;

using System.Globalization;
using System.Text.Json;

internal static class BulkDeleteTranscript
{
    public static byte[] Create(IEnumerable<(ulong AuthorId, ulong ChannelId, string Content)> messages)
    {
        using var stream = new MemoryStream();

        foreach (var message in messages)
        {
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString("author_id", message.AuthorId.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("channel_id", message.ChannelId.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("content", message.Content);
            writer.WriteEndObject();
            writer.Flush();
            stream.WriteByte((byte)'\n');
        }

        return stream.ToArray();
    }
}