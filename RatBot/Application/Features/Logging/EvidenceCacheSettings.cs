namespace RatBot.Application.Features.Logging;

public sealed record EvidenceCacheSettings(
    int MaxCachedMessageCount,
    int MaxAttachmentCountPerMessage,
    long MaxBytesPerAttachment,
    long MaxTotalCachedAttachmentBytes
)
{
    public void Validate()
    {
        if (MaxCachedMessageCount <= 0)
            throw new InvalidOperationException("Max cached message count must be positive.");

        if (MaxAttachmentCountPerMessage < 0)
            throw new InvalidOperationException("Max attachment count per message must not be negative.");

        if (MaxBytesPerAttachment < 0)
            throw new InvalidOperationException("Max bytes per attachment must not be negative.");

        if (MaxTotalCachedAttachmentBytes < 0)
            throw new InvalidOperationException("Max total cached attachment bytes must not be negative.");
    }
}