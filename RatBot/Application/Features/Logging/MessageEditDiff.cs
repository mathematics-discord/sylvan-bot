namespace RatBot.Application.Features.Logging;

public sealed record MessageEditDiff(
    ImmutableArray<MessageEditDiffSegment> Before,
    ImmutableArray<MessageEditDiffSegment> After);