namespace SylvanBot.Application.Features.SylvanMail;

public interface ISylvanMailDiscord
{
    Task ValidateForumAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct, bool validatePing = true);

    Task<bool> CanWriteAsync(ulong guildId, ulong threadId, ulong authorId, CancellationToken ct);

    Task<SylvanMailMessage?> ReadDraftAsync(ulong threadId, ulong messageId, ulong authorId, bool confirm, CancellationToken ct);

    Task SetDraftReadyAsync(ulong threadId, ulong messageId, bool ready, CancellationToken ct);

    Task MarkDeliveredAsync(SylvanMailMessage message, CancellationToken ct);

    Task<string> InboundPrefixAsync(ulong guildId, SylvanMailMessage message, CancellationToken ct);

    Task PingAfterGapAsync(SylvanMailSettings settings, ulong threadId, SylvanMailMessage message, CancellationToken ct);

    Task<ulong?> FindPostAsync(SylvanMailConversation conversation, CancellationToken ct);

    Task<ulong> CreatePostAsync(SylvanMailConversation conversation, ulong roleId, string username, CancellationToken ct);

    // False means the mapped post is gone, inaccessible, or outside the configured forum.
    Task<bool> OpenPostAsync(SylvanMailConversation conversation, CancellationToken ct);

    Task<ulong> OpenDmAsync(ulong userId, CancellationToken ct);

    Task<ulong> SendTextAsync(ulong channelId, string text, CancellationToken ct);

    Task<ulong> SendAttachmentAsync(ulong channelId, SylvanMailAttachment attachment, byte[] bytes, string label, CancellationToken ct);

    Task<byte[]> DownloadAsync(SylvanMailAttachment attachment, CancellationToken ct);

    Task<ulong?> ReportAsync(SylvanMailMessage source, ulong? threadId, SylvanMailOperation? operation, string detail, CancellationToken ct);

    Task<ulong?> AcknowledgeAsync(SylvanMailMessage source, bool first, CancellationToken ct);
}