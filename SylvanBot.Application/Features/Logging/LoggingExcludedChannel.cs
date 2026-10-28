namespace SylvanBot.Application.Features.Logging;

public sealed class LoggingExcludedChannel
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public DateTimeOffset ExcludedAtUtc { get; set; }
}
