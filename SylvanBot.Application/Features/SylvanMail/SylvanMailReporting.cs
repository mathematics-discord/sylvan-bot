using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailReporting(ISylvanMailDiscord discord, ISylvanMailDrafts drafts)
{
    public async Task ReportAsync(BotDbContext db, SylvanMailMessage message, ulong? threadId, SylvanMailOperation operation)
    {
        // Outcome notices still get a bounded attempt when the delivery token has timed out.
        using CancellationTokenSource noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken noticeToken = noticeTimeout.Token;

        if (operation.State != SylvanMailDeliveryState.Delivered)
        {
            Optional<ulong> thread = threadId is { } target ? new Optional<ulong>(target) : default;
            Optional<ulong> previousReceipt = operation.ReceiptMessageId is { } previous ? new Optional<ulong>(previous) : default;

            Optional<ulong> receipt = await discord.ReportAsync(
                message,
                threadId: thread,
                receiptId: previousReceipt,
                operation.Detail,
                noticeToken);
            operation.ReceiptMessageId = receipt is { HasValue: true, Value: var receiptId } ? receiptId : null;
            await db.SaveChangesAsync(noticeToken);
            return;
        }

        if (!operation.ToStaff)
        {
            await drafts.MarkDeliveredAsync(message, noticeToken);
            return;
        }

        if (operation.AcknowledgementStarted)
            return;

        var previouslyAcknowledged = await db.SylvanMailOperations
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.GuildId == operation.GuildId
                    && item.SourceChannelId == message.SourceChannelId
                    && item.ToStaff
                    && item.AcknowledgementStarted,
                cancellationToken: noticeToken);

        // Persist the start so a crash after Discord accepts the acknowledgement cannot trigger a second one.
        operation.AcknowledgementStarted = true;
        await db.SaveChangesAsync(noticeToken);

        Optional<ulong> acknowledgement = await discord.AcknowledgeAsync(message, first: !previouslyAcknowledged, noticeToken);
        operation.AcknowledgementMessageId = acknowledgement is { HasValue: true, Value: var acknowledgementId } ? acknowledgementId : null;
        await db.SaveChangesAsync(noticeToken);
    }
}