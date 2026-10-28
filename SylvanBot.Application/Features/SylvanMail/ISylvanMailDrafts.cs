using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.SylvanMail;

public interface ISylvanMailDrafts
{
    Task<Optional<SylvanMailMessage>> ReadDraftAsync(ulong threadId, ulong messageId, ulong authorId, bool confirm, CancellationToken ct);

    Task SetDraftReadyAsync(ulong threadId, ulong messageId, bool ready, CancellationToken ct);

    Task MarkDeliveredAsync(SylvanMailMessage message, CancellationToken ct);
}