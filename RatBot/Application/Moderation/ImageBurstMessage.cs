namespace RatBot.Application.Moderation;

public sealed record ImageBurstMessage(
    ulong GuildId,
    ulong UserId,
    ulong ChannelId,
    DateTimeOffset Timestamp,
    IReadOnlyList<ImageBurstAttachment> Attachments
);