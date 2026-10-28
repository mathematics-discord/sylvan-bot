using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.SylvanMail;

public interface ISylvanMailForum
{
    Task ValidateForumAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct, bool validatePing = true);

    Task<Optional<ulong>> FindPostAsync(ulong guildId, ulong forumId, long number, CancellationToken ct);

    Task<ulong> CreatePostAsync(SylvanMailPost post, ulong roleId, string username, CancellationToken ct);

    Task<bool> OpenPostAsync(ulong guildId, ulong forumId, ulong threadId, CancellationToken ct);
}