using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.SylvanMail;

public interface ISylvanMailDiscord
{
    Task<string> InboundPrefixAsync(ulong guildId, SylvanMailMessage message, CancellationToken ct);

    Task PingAfterGapAsync(ulong guildId, ulong roleId, ulong threadId, SylvanMailMessage message, CancellationToken ct);

    Task<ulong> OpenDmAsync(ulong userId, CancellationToken ct);

    Task<ulong> SendTextAsync(ulong channelId, string text, CancellationToken ct);

    Task<ulong> SendAttachmentAsync(ulong channelId, SylvanMailAttachment attachment, byte[] bytes, string label, CancellationToken ct);

    Task<byte[]> DownloadAsync(SylvanMailAttachment attachment, CancellationToken ct);

    Task<Option<ulong>> ReportAsync(SylvanMailMessage source, Option<ulong> threadId, Option<ulong> receiptId, string detail, CancellationToken ct);

    Task<Option<ulong>> AcknowledgeAsync(SylvanMailMessage source, bool first, CancellationToken ct);
}