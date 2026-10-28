namespace SylvanBot.Features.Logging;

public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    public int MaxAttachmentCountPerMessage { get; init; } = 4;
    public long MaxAttachmentBytesPerAttachment { get; init; } = 8 * 1024 * 1024;
}