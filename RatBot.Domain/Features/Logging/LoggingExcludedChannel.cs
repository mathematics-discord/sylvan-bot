namespace RatBot.Domain.Features.Logging;

public sealed class LoggingExcludedChannel
{
    private LoggingExcludedChannel()
    {
    }

    public LoggingExcludedChannel(ulong guildId, ulong channelId, DateTimeOffset excludedAtUtc)
    {
        if (guildId == 0)
            throw new ArgumentOutOfRangeException(nameof(guildId), "Guild id is required.");

        if (channelId == 0)
            throw new ArgumentOutOfRangeException(nameof(channelId), "Channel id is required.");

        GuildId = guildId;
        ChannelId = channelId;
        ExcludedAtUtc = excludedAtUtc;
    }

    public ulong GuildId { get; }
    public ulong ChannelId { get; }
    public DateTimeOffset ExcludedAtUtc { get; }
}