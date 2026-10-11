namespace RatBot.Domain.Features.Logging;

public sealed class ObservedMessage
{
    private ObservedMessage()
    {
    }

    public ObservedMessage(ulong originalMessageId, ulong guildId, ulong channelId, ulong authorId,
        DateTimeOffset observedAtUtc)
    {
        if (originalMessageId == 0)
            throw new ArgumentOutOfRangeException(nameof(originalMessageId), "Message id is required.");

        if (guildId == 0)
            throw new ArgumentOutOfRangeException(nameof(guildId), "Guild id is required.");

        if (channelId == 0)
            throw new ArgumentOutOfRangeException(nameof(channelId), "Channel id is required.");

        if (authorId == 0)
            throw new ArgumentOutOfRangeException(nameof(authorId), "Author id is required.");

        OriginalMessageId = originalMessageId;
        GuildId = guildId;
        ChannelId = channelId;
        AuthorId = authorId;
        ObservedAtUtc = observedAtUtc;
    }

    public ulong OriginalMessageId { get; private set; }
    public ulong GuildId { get; private set; }
    public ulong ChannelId { get; private set; }
    public ulong AuthorId { get; private set; }
    public DateTimeOffset ObservedAtUtc { get; private set; }

    public ObservedMessage MoveTo(ulong guildId, ulong channelId, ulong authorId, DateTimeOffset observedAtUtc)
    {
        return new ObservedMessage(OriginalMessageId, guildId, channelId, authorId, observedAtUtc);
    }
}