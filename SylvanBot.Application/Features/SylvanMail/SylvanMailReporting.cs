using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailReporting(ISylvanMailDiscord discord, ISylvanMailDrafts drafts)
{
    public async Task ReportAsync(BotDbContext db, SylvanMailMessage message, ulong? threadId, SylvanMailOperation op)
    {
        // Outcome notices still get a bounded attempt when the delivery token has timed out.
        using var noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var noticeToken = noticeTimeout.Token;

        if (string.Equals(a: op.State, "Delivered", comparisonType: StringComparison.Ordinal))
        {
            if (!op.ToStaff)
            {
                await drafts.MarkDeliveredAsync(message: message, ct: noticeToken);

                return;
            }

            if (op.AcknowledgementStarted)
                return;

            var previouslyAcknowledged = await db.SylvanMailOperations
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.GuildId == op.GuildId
                        && item.ConversationId == op.ConversationId
                        && item.ToStaff
                        && item.AcknowledgementMessageId != null,
                    cancellationToken: noticeToken);
            op.AcknowledgementStarted = true;
            await db.SaveChangesAsync(noticeToken);
            Option<ulong> acknowledgement = await discord.AcknowledgeAsync(source: message, first: !previouslyAcknowledged, ct: noticeToken);
            op.AcknowledgementMessageId = acknowledgement.Value is Some<ulong>(var acknowledgementId) ? acknowledgementId : null;
            await db.SaveChangesAsync(noticeToken);

            return;
        }

        Option<ulong> thread = threadId is { } target ? new Some<ulong>(target) : new None();
        Option<ulong> previousReceipt = op.ReceiptMessageId is { } previous ? new Some<ulong>(previous) : new None();
        Option<ulong> receipt = await discord.ReportAsync(message, thread, previousReceipt, op.Detail, noticeToken);
        op.ReceiptMessageId = receipt.Value is Some<ulong>(var receiptId) ? receiptId : null;
        await db.SaveChangesAsync(noticeToken);
    }
}