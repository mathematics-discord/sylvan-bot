namespace SylvanBot.Application.Features.SylvanMail;

public interface ISylvanMailDiscord
{
    Task ValidateForumAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct);

    Task<bool> IsStaffAsync(ulong guildId, ulong authorId, ulong roleId, CancellationToken ct);

    Task<ulong?> FindPostAsync(SylvanMailConversation conversation, CancellationToken ct);

    Task<ulong> CreatePostAsync(SylvanMailConversation conversation, ulong roleId, string username, CancellationToken ct);

    // False means a confirmed Unknown Channel response, never a cache miss or access failure.
    Task<bool> OpenPostAsync(SylvanMailConversation conversation, CancellationToken ct);

    Task<ulong> OpenDmAsync(ulong userId, CancellationToken ct);

    Task<ulong> SendTextAsync(ulong channelId, string text, CancellationToken ct);

    Task<ulong> SendAttachmentAsync(ulong channelId, SylvanMailAttachment attachment, byte[] bytes, string label, CancellationToken ct);

    Task<byte[]> DownloadAsync(SylvanMailAttachment attachment, CancellationToken ct);

    Task<ulong?> ReportAsync(SylvanMailMessage source, ulong? threadId, SylvanMailOperation? operation, string detail, CancellationToken ct);

    Task<ulong?> AcknowledgeAsync(SylvanMailMessage source, bool first, CancellationToken ct);
}
