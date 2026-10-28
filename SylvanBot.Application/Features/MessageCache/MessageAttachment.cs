#pragma warning disable SA1313
namespace SylvanBot.Application.Features.MessageCache;

public sealed record MessageAttachment(string Url, string? ProxyUrl, string? ContentType, long Size);
