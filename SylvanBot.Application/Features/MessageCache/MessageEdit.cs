using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.MessageCache;

public sealed record MessageEdit
{
    public required Option<CachedMessageSnapshot> Before { get; init; }

    public required CachedMessageSnapshot After { get; init; }
}