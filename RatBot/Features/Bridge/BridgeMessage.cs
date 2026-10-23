namespace RatBot.Features.Bridge;

public sealed record BridgeMessage(
    string MessageId,
    string GuildId,
    string ChannelId,
    DateTimeOffset Timestamp,
    string AuthorId,
    string DisplayName,
    string Text,
    BridgeReference? ReplyReference,
    ImmutableArray<BridgeAttachment> Attachments);
