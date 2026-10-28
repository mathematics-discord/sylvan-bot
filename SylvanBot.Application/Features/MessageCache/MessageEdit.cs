namespace SylvanBot.Application.Features.MessageCache;

public sealed record MessageEdit
{
    public required Optional<CachedMessageSnapshot> Before { get; init; }

    public required CachedMessageSnapshot After { get; init; }
}