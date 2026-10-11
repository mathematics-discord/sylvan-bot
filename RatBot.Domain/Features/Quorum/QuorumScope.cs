namespace RatBot.Domain.Features.Quorum;

public abstract record QuorumScope(ulong GuildId, ulong ChannelId)
{
    public sealed record ForumChannel(ulong GuildId, ulong ChannelId) : QuorumScope(GuildId, ChannelId);

    public sealed record TextChannel(ulong GuildId, ulong ChannelId) : QuorumScope(GuildId, ChannelId);
}