namespace SylvanBot.Features.Moderation;

public sealed record ImageBurstSpamDetectorOptions
{
    public int DistinctChannelThreshold { get; init; } = 4;

    public TimeSpan HandlingLockDuration { get; init; } = TimeSpan.FromMinutes(3);

    public int RequiredAttachmentCount { get; init; } = 2;
    public int Window { get; init; } = 45;
}