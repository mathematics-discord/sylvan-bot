namespace RatBot.Domain.Features.Logging;

public sealed class MessageLogEntry
{
    private MessageLogEntry()
    {
    }

    public MessageLogEntry(ulong guildId, ulong originalMessageId, ulong logMessageId, DateTimeOffset capturedAtUtc)
    {
        if (guildId == 0)
            throw new ArgumentOutOfRangeException(nameof(guildId), "Guild id is required.");

        if (originalMessageId == 0)
            throw new ArgumentOutOfRangeException(nameof(originalMessageId), "Original message id is required.");

        if (logMessageId == 0)
            throw new ArgumentOutOfRangeException(nameof(logMessageId), "Log message id is required.");

        GuildId = guildId;
        OriginalMessageId = originalMessageId;
        LogMessageId = logMessageId;
        CapturedAtUtc = capturedAtUtc;
    }

    public ulong GuildId { get; private set; }
    public ulong OriginalMessageId { get; private set; }
    public ulong LogMessageId { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }
}