namespace RatBot.Application.Features.Logging;

public sealed record MessageEvidence(
    ulong GuildId,
    ulong ChannelId,
    ulong MessageId,
    ulong AuthorId,
    DateTimeOffset CapturedAtUtc,
    string? Content,
    IReadOnlyList<CachedAttachmentEvidence> Attachments
)
{
    public ImmutableArray<ulong> SourceAttachmentIds { get; init; } = ImmutableArray<ulong>.Empty;
}