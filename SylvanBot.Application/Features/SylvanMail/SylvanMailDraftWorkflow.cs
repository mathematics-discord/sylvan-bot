using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailDraftWorkflow(
    IDbContextFactory<BotDbContext> contexts,
    ISylvanMailDrafts drafts,
    ISylvanMailDiscord discord,
    SylvanMailWorkflow relay
)
{
    public async Task HandleDraftAsync(ulong threadId, ulong messageId, ulong authorId, bool confirm, CancellationToken ct, bool cancel = false)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await SylvanMailSerialisation.EnterAsync(db: db, ct: ct);
        Optional<SylvanMailMessage> observed = default;

        try
        {
            var conversation = await ActiveConversationAsync(db: db, threadId: threadId, ct: ct);

            if (conversation is null)
                return;

            observed = await drafts.ReadDraftAsync(threadId: threadId, messageId: messageId, authorId: authorId, confirm: confirm || cancel, ct: ct);

            if (observed is not { HasValue: true, Value: var message })
                return;

            if (cancel)
            {
                var recorded = await db.SylvanMailOperations
                   .AnyAsync(item => item.SourceMessageId == messageId, cancellationToken: ct);

                if (recorded)
                    throw new BotCommandException("Cannot cancel: delivery has already been attempted. Check what arrived before sending another message.");

                await drafts.ClearReactionsAsync(threadId: threadId, messageId: messageId, ct: ct);
            }
            else
                await ProcessDraftAsync(db: db, message: message, confirm: confirm, ct: ct);
        }
        catch (Exception exception) when (observed is { HasValue: true, Value: var message })
        {
            using var noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            if (confirm || cancel)
            {
                var detail = "Delivery could not be confirmed. Some content may have arrived; do not assume it is safe to resend.";

                if (cancel)
                    detail = "Cancellation could not be confirmed. Remove and re-add your cancel reaction to try again; contact staff if it continues to fail.";

                if (exception is BotCommandException)
                    detail = exception.Message;

                await discord.ReportAsync(
                    source: message,
                    new Optional<ulong>(threadId),
                    receiptId: default,
                    detail: detail,
                    ct: noticeTimeout.Token);
            }

            if (!cancel)
                await drafts.SetDraftReadyAsync(threadId: threadId, messageId: messageId, ready: false, ct: noticeTimeout.Token);

            if (exception is not BotCommandException)
                throw;
        }
        finally
        {
            await SylvanMailSerialisation.ExitAsync(db);
        }
    }

    public async Task RefuseConfirmationAsync(ulong threadId, ulong messageId, ulong authorId, CancellationToken ct, bool cancel = false)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var conversation = await ActiveConversationAsync(db: db, threadId: threadId, ct: ct);

        if (conversation is null)
            return;

        Optional<SylvanMailMessage> observed = await drafts.ReadDraftAsync(
            threadId: threadId,
            messageId: messageId,
            authorId: authorId,
            confirm: true,
            ct: ct);

        if (observed is { HasValue: true, Value: var message })
        {
            if (cancel)
                await discord.ReportAsync(
                    source: message,
                    new Optional<ulong>(threadId),
                    receiptId: default,
                    "Not cancelled: SylvanMail is temporarily busy. Remove and re-add your cancel reaction to try again.",
                    ct: ct);
            else
                await relay.RefuseBusyAsync(message: message, ct: ct);
        }
    }

    private static Task<SylvanMailConversation?> ActiveConversationAsync(BotDbContext db, ulong threadId, CancellationToken ct) =>
        db.SylvanMailConversations
           .AsNoTracking()
           .Where(item => item.ThreadId == threadId && !item.Retired)
           .Where(item => db.SylvanMailSettings
                     .Any(settings => settings.GuildId == item.GuildId && settings.ForumId == item.ForumId))
           .SingleOrDefaultAsync(ct);

    private async Task ProcessDraftAsync(BotDbContext db, SylvanMailMessage message, bool confirm, CancellationToken ct)
    {
        var recorded = await db.SylvanMailOperations
           .AnyAsync(item => item.SourceMessageId == message.SourceMessageId, cancellationToken: ct);

        if (recorded)
        {
            if (confirm)
                await relay.RelayLockedAsync(db: db, message: message, ct: ct);

            await drafts.SetDraftReadyAsync(threadId: message.SourceChannelId, messageId: message.SourceMessageId, ready: false, ct: ct);

            return;
        }

        if (string.IsNullOrEmpty(message.OutboundPrefix))
            throw new BotCommandException("Not sent: mention Sylvan or reply to Sylvan with the reply ping enabled before confirming.");

        SylvanMailContent.Validate(message);

        if (confirm)
        {
            await relay.RelayLockedAsync(db: db, message: message, ct: ct);
            await drafts.SetDraftReadyAsync(threadId: message.SourceChannelId, messageId: message.SourceMessageId, ready: false, ct: ct);
        }
        else
            await drafts.SetDraftReadyAsync(threadId: message.SourceChannelId, messageId: message.SourceMessageId, ready: true, ct: ct);
    }
}
