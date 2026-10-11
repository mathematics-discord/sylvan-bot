namespace RatBot.Application.Moderation;

public sealed record ImageBurstDetection(
    ulong GuildId,
    ulong UserId,
    IReadOnlyList<ImageBurstMessage> Messages,
    IReadOnlyList<ulong> ChannelIds);