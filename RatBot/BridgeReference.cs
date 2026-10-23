namespace RatBot.Features.Bridge;

public sealed record BridgeReference
{
    public BridgeReference(string? messageId, string channelId, string? guildId)
    {
        this.MessageId = messageId;
        this.ChannelId = channelId;
        this.GuildId = guildId;
    }

    public string? MessageId { get; init; }
    public string ChannelId { get; init; }
    public string? GuildId { get; init; }

    public void Deconstruct(out string? messageId, out string channelId, out string? guildId)
    {
        messageId = this.MessageId;
        channelId = this.ChannelId;
        guildId = this.GuildId;
    }
}
