namespace SylvanBot.Application.Features.Logging;

public sealed class LoggingExcludedChannel
{
    public ulong GuildId { get; init; }

    public ulong ChannelId { get; init; }

    public DateTimeOffset ExcludedAtUtc { get; init; }
}