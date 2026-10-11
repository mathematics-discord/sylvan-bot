namespace RatBot.Application.Features.Logging;

public sealed record CachedAttachmentEvidence(int Index, byte[] Bytes, string ContentType);