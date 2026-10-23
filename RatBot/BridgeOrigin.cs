namespace RatBot.Features.Bridge;

public sealed record BridgeOrigin
{
    public BridgeOrigin(ulong messageId, ulong guildId, ulong channelId)
    {
        this.MessageId = messageId;
        this.GuildId = guildId;
        this.ChannelId = channelId;
    }

    public ulong MessageId { get; init; }
    public ulong GuildId { get; init; }
    public ulong ChannelId { get; init; }

    public void Deconstruct(out ulong messageId, out ulong guildId, out ulong channelId)
    {
        messageId = this.MessageId;
        guildId = this.GuildId;
        channelId = this.ChannelId;
    }
}