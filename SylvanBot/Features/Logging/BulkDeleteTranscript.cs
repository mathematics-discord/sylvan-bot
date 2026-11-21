using System.Globalization;
using System.Text;

namespace SylvanBot.Features.Logging;

internal static class BulkDeleteTranscript
{
    public static byte[] Create(IEnumerable<(DateTimeOffset Timestamp, string Channel, string Username, string Content)> messages)
    {
        StringBuilder transcript = new StringBuilder();

        foreach ((DateTimeOffset Timestamp, string Channel, string Username, string Content) message in messages)
        {
            var timestamp = message.Timestamp.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var channel = string.Join(" ", message.Channel.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var username = string.Join(" ", message.Username.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            var lines = message.Content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace('\u2028', '\n')
                .Replace('\u2029', '\n')
                .Split('\n');

            foreach (var line in lines)
                transcript.Append(CultureInfo.InvariantCulture, $"[{timestamp}] #{channel} {username}: {line}\n");
        }

        return Encoding.UTF8.GetBytes(transcript.ToString());
    }
}