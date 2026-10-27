using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailWorkflow(IDbContextFactory<BotDbContext> contexts, ISylvanMailDiscord discord)
{
    // One feature-local database lock also serializes configuration with in-flight relay work.
    private const long LockId             = 739416284;
    private const int  MaxAttachmentBytes = 8 * 1024 * 1024;

    public async Task ConfigureAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await LockAsync(db: db, ct: ct);

        try
        {
            await discord.ValidateForumAsync(guildId: guildId, forumId: forumId, roleId: roleId, ct: ct);

            SylvanMailSettings[] settings = await db.SylvanMailSettings
               .Take(2)
               .ToArrayAsync(ct);

            var conflicting = settings
               .Any(item => item.GuildId != guildId);

            if (settings.Length > 1 || conflicting)
                throw new BotCommandException("SylvanMail already has a different guild destination. No settings changed.");

            var saved = settings.SingleOrDefault();

            if (saved is null)
            {
                saved = new SylvanMailSettings { GuildId = guildId };
                db.SylvanMailSettings.Add(saved);
            }

            saved.ForumId = forumId;
            saved.RoleId  = roleId;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            await UnlockAsync(db);
        }
    }

    public async Task HandleDraftAsync(ulong threadId, ulong messageId, ulong authorId, bool confirm, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await LockAsync(db: db, ct: ct);
        SylvanMailMessage? message = null;

        try
        {
            var conversation = await ActiveConversationAsync(db: db, threadId: threadId, ct: ct);

            if (conversation is null)
                return;

            message = await discord.ReadDraftAsync(threadId: threadId, messageId: messageId, authorId: authorId, confirm: confirm, ct: ct);

            if (message is null)
                return;

            var recorded = await db.SylvanMailOperations
               .AnyAsync(item => item.SourceMessageId == messageId, cancellationToken: ct);

            if (recorded)
            {
                if (confirm)
                    await RelayLockedAsync(db: db, message: message, ct: ct);

                await discord.SetDraftReadyAsync(threadId: threadId, messageId: messageId, ready: false, ct: ct);

                return;
            }

            if (!await discord.CanWriteAsync(guildId: conversation.GuildId, threadId: threadId, authorId: authorId, ct: ct))
                throw new BotCommandException("Not sent: you no longer have write access to this thread.");

            if (string.IsNullOrEmpty(message.OutboundPrefix))
                throw new BotCommandException("Not sent: mention Sylvan or reply to Sylvan with the reply ping enabled before confirming.");

            ValidateContent(message);

            if (confirm)
            {
                await RelayLockedAsync(db: db, message: message, ct: ct);
                await discord.SetDraftReadyAsync(threadId: threadId, messageId: messageId, ready: false, ct: ct);
            }
            else
                await discord.SetDraftReadyAsync(threadId: threadId, messageId: messageId, ready: true, ct: ct);
        }
        catch (Exception exception) when (message is not null)
        {
            using var noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            if (confirm)
            {
                var detail = exception is BotCommandException
                    ? exception.Message
                    : "Delivery could not be confirmed. Some content may have arrived; do not assume it is safe to resend.";
                await discord.ReportAsync(source: message, threadId: threadId, operation: null, detail: detail, ct: noticeTimeout.Token);
            }

            await discord.SetDraftReadyAsync(threadId: threadId, messageId: messageId, ready: false, ct: noticeTimeout.Token);
        }
        finally
        {
            await UnlockAsync(db);
        }
    }

    public async Task RefuseConfirmationAsync(ulong threadId, ulong messageId, ulong authorId, CancellationToken ct)
    {
        await using var db           = await contexts.CreateDbContextAsync(ct);
        var             conversation = await ActiveConversationAsync(db: db, threadId: threadId, ct: ct);

        if (conversation is null)
            return;

        var message = await discord.ReadDraftAsync(threadId: threadId, messageId: messageId, authorId: authorId, confirm: true, ct: ct);

        if (message is not null)
            await RefuseBusyAsync(message: message, ct: ct);
    }

    public async Task RelayAsync(SylvanMailMessage message, CancellationToken ct)
    {
        // Outbound delivery is reachable only through author confirmation and a fresh draft read.
        if (message.GuildId is not null)
            return;

        await using var db = await contexts.CreateDbContextAsync(ct);
        await LockAsync(db: db, ct: ct);

        try
        {
            await RelayLockedAsync(db: db, message: message, ct: ct);
        }
        finally
        {
            await UnlockAsync(db);
        }
    }

    public async Task RefuseBusyAsync(SylvanMailMessage message, CancellationToken ct)
    {
        // Do not wait behind relay's Discord calls when the transient buffer is full.
        await using var db = await contexts.CreateDbContextAsync(ct);

        SylvanMailSettings[] settings = await db.SylvanMailSettings
           .AsNoTracking()
           .Take(2)
           .ToArrayAsync(ct);

        if (settings.Length != 1)
        {
            if (message.GuildId is null)
                await discord.ReportAsync(source: message, threadId: null, operation: null, "SylvanMail is unavailable; your message was not relayed.", ct: ct);

            return;
        }

        var destination = settings[0];
        var toStaff     = message.GuildId is null;

        IQueryable<SylvanMailConversation> conversationQuery = db.SylvanMailConversations
           .AsNoTracking()
           .Where(item => item.GuildId == destination.GuildId && item.ForumId == destination.ForumId && !item.Retired);

        var conversation = toStaff
            ? await conversationQuery.SingleOrDefaultAsync(item => item.UserId == message.AuthorId, cancellationToken: ct)
            : await conversationQuery.SingleOrDefaultAsync(item => item.ThreadId == message.SourceChannelId, cancellationToken: ct);

        if (!toStaff)
        {
            var authorised = message.GuildId == destination.GuildId
                && conversation is not null
                && await discord.CanWriteAsync(guildId: destination.GuildId, threadId: message.SourceChannelId, authorId: message.AuthorId, ct: ct);

            if (!authorised)
                return;
        }

        var recorded = await db.SylvanMailOperations
           .AnyAsync(item => item.SourceMessageId == message.SourceMessageId, cancellationToken: ct);

        if (recorded)
            return;

        var operation = new SylvanMailOperation
        {
            SourceMessageId = message.SourceMessageId,
            SourceChannelId = message.SourceChannelId,
            AuthorId        = message.AuthorId,
            GuildId         = destination.GuildId,
            ConversationId  = conversation?.Id,
            ToStaff         = toStaff,
            State           = "Refused",
            Detail          = "Not relayed: SylvanMail is temporarily busy. This message was refused; please send it again later.",
        };

        db.SylvanMailOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        await ReportAsync(db: db, message: message, threadId: conversation?.ThreadId, op: operation);
    }

    private static Task<SylvanMailConversation?> ActiveConversationAsync(BotDbContext db, ulong threadId, CancellationToken ct) =>
        db.SylvanMailConversations
           .AsNoTracking()
           .Where(item => item.ThreadId == threadId && !item.Retired)
           .Where(item => db.SylvanMailSettings.Any(settings => settings.GuildId == item.GuildId && settings.ForumId == item.ForumId))
           .SingleOrDefaultAsync(ct);

    private static async Task LockAsync(BotDbContext db, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({LockId})", cancellationToken: ct);
    }

    private static Task UnlockAsync(BotDbContext db) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({LockId})", cancellationToken: CancellationToken.None);

    private static void ValidateContent(SylvanMailMessage message)
    {
        var totalBytes = message.Attachments
           .Sum(item => (long)item.Size);

        var oversized = message.Attachments
           .Any(item => item.Size is < 0 or > MaxAttachmentBytes);

        if (message.UnsupportedContent)
            throw new BotCommandException("Not relayed: stickers, polls, voice messages, forwards and interactive content are unsupported. Send text or ordinary attachments.");

        if (message.Content.Length > 16000 || message.Attachments.Length > 10 || oversized || totalBytes > 24 * 1024 * 1024)
            throw new BotCommandException("Not relayed: maximum 16,000 text characters, 10 attachments, 8 MiB per file and 24 MiB total.");

        if (string.IsNullOrWhiteSpace(message.Content) && message.Attachments.IsEmpty)
            throw new BotCommandException("Not relayed: no supported text or attachments remain after removing Sylvan's mention.");
    }

    private static ImmutableArray<string> SplitContent(string content, int size)
    {
        var chunks = ImmutableArray.CreateBuilder<string>();
        var index  = 0;

        while (index < content.Length)
        {
            var length = Math.Min(val1: size, content.Length - index);

            if (index + length < content.Length && char.IsHighSurrogate(content[(index + length) - 1]))
                length--;

            chunks.Add(content.Substring(startIndex: index, length: length));
            index += length;
        }

        return chunks.ToImmutable();
    }

    private static async Task PublishAsync(BotDbContext db, SylvanMailOperation op, int ordinal, Func<Task<ulong>> publish, CancellationToken ct)
    {
        var delivery = new SylvanMailDelivery { SourceMessageId = op.SourceMessageId, Ordinal = ordinal };
        op.Deliveries.Add(delivery);
        op.State = "Sending";
        await db.SaveChangesAsync(ct);
        delivery.MessageId = await publish();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Discord accepted it but the durable confirmation failed
            delivery.MessageId = null;

            throw;
        }
    }

    private async Task RelayLockedAsync(BotDbContext db, SylvanMailMessage message, CancellationToken ct)
    {
        SylvanMailSettings[] destinations = await db.SylvanMailSettings
           .AsNoTracking()
           .Take(2)
           .ToArrayAsync(ct);

        if (destinations.Length != 1)
        {
            if (message.GuildId is null)
                await discord.ReportAsync(source: message, threadId: null, operation: null, "SylvanMail is unavailable; your message was not relayed.", ct: ct);

            return;
        }

        var                     settings     = destinations[0];
        var                     toStaff      = message.GuildId is null;
        SylvanMailConversation? conversation = null;

        if (toStaff)
        {
            conversation = await db.SylvanMailConversations
               .SingleOrDefaultAsync(
                    item => item.GuildId == settings.GuildId
                        && item.ForumId == settings.ForumId
                        && item.UserId == message.AuthorId
                        && !item.Retired,
                    cancellationToken: ct);
        }
        else
        {
            if (message.GuildId != settings.GuildId)
                return;

            conversation = await db.SylvanMailConversations
               .SingleOrDefaultAsync(
                    item => item.GuildId == settings.GuildId
                        && item.ForumId == settings.ForumId
                        && item.ThreadId == message.SourceChannelId
                        && !item.Retired,
                    cancellationToken: ct);

            if (conversation is null)
                return;

            if (!await discord.CanWriteAsync(guildId: settings.GuildId, threadId: message.SourceChannelId, authorId: message.AuthorId, ct: ct))
                throw new BotCommandException("Not sent: you no longer have write access to this thread.");
        }

        var operation = await db.SylvanMailOperations
           .Include(item => item.Deliveries)
           .SingleOrDefaultAsync(item => item.SourceMessageId == message.SourceMessageId, cancellationToken: ct);

        if (operation is not null)
        {
            var previousThread = await db.SylvanMailConversations
               .Where(item => item.Id == operation.ConversationId)
               .Select(item => item.ThreadId)
               .SingleOrDefaultAsync(ct);

            if (string.Equals(a: operation.State, "Sending", comparisonType: StringComparison.Ordinal))
            {
                operation.State  = "Uncertain";
                operation.Detail = "Interrupted delivery: some content may have arrived. No automatic resend.";
                await db.SaveChangesAsync(ct);
            }

            if (string.Equals(a: operation.State, "Saved", comparisonType: StringComparison.Ordinal))
            {
                operation.State  = "Unpublished";
                operation.Detail = "Interrupted before delivery started. No automatic resend.";
                await db.SaveChangesAsync(ct);
            }

            await ReportAsync(db: db, message: message, threadId: previousThread, op: operation);

            return;
        }

        var previousThreadId = conversation?.ThreadId;

        operation = new SylvanMailOperation
        {
            SourceMessageId = message.SourceMessageId,
            SourceChannelId = message.SourceChannelId,
            AuthorId        = message.AuthorId,
            GuildId         = settings.GuildId,
            ConversationId  = conversation?.Id,
            ToStaff         = toStaff,
        };

        db.SylvanMailOperations.Add(operation);
        await db.SaveChangesAsync(ct);

        try
        {
            await discord.ValidateForumAsync(guildId: settings.GuildId, forumId: settings.ForumId, roleId: settings.RoleId, ct: ct, validatePing: false);
            ValidateContent(message);

            // Download all attachments before publishing any of this message; retain at most 24 MiB of attachment payload.
            var attachments = ImmutableArray.CreateBuilder<byte[]>(message.Attachments.Length);

            foreach (var attachment in message.Attachments)
                attachments.Add(await discord.DownloadAsync(attachment: attachment, ct: ct));

            if (toStaff)
                conversation = await ResolveConversationAsync(db: db, settings: settings, message: message, operation: operation, ct: ct);
            else if (!await discord.OpenPostAsync(conversation!, ct: ct))
                throw new BotCommandException("The mapped post was deleted; this reply was not sent.");

            var threadId = conversation!.ThreadId!.Value;
            operation.DestinationChannelId = toStaff ? threadId : await discord.OpenDmAsync(userId: conversation.UserId, ct: ct);
            await db.SaveChangesAsync(ct);

            if (toStaff && !conversation.NoticeStarted)
            {
                conversation.NoticeStarted = true;
                await db.SaveChangesAsync(ct);

                conversation.NoticeMessageId = await discord.SendTextAsync(
                    channelId: message.SourceChannelId,
                    "SylvanMail is in beta: bugs and lost messages are possible. Your DMs to Sylvan reach staff in a private forum post. Staff replies are sent by Sylvan after the author confirms them; edits and deletions after sending are not relayed.",
                    ct: ct);

                await db.SaveChangesAsync(ct);
            }

            // An uncertain notice is not retried, but subsequent messages can still be delivered.
            var                    label   = toStaff ? await discord.InboundPrefixAsync(guildId: settings.GuildId, message: message, ct: ct) : message.OutboundPrefix;
            ImmutableArray<string> chunks  = SplitContent(content: message.Content, 2000 - label.Length);
            var                    ordinal = 0;

            foreach (var chunk in chunks)
                await PublishAsync(
                    db: db,
                    op: operation,
                    ordinal: ordinal++,
                    () => discord.SendTextAsync(channelId: operation.DestinationChannelId.Value, label + chunk, ct: ct),
                    ct: ct);

            for (var index = 0; index < message.Attachments.Length; index++)
            {
                var attachment = message.Attachments[index];
                var bytes      = attachments[index];

                await PublishAsync(
                    db: db,
                    op: operation,
                    ordinal: ordinal++,
                    () => discord.SendAttachmentAsync(channelId: operation.DestinationChannelId.Value, attachment: attachment, bytes: bytes, label: label, ct: ct),
                    ct: ct);
            }

            operation.State  = "Delivered";
            operation.Detail = "Delivered: Discord accepted every part.";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            if (conversation is null && operation.ConversationId.HasValue)
            {
                conversation = await db.SylvanMailConversations
                   .SingleOrDefaultAsync(item => item.Id == operation.ConversationId, cancellationToken: CancellationToken.None);
            }

            var uncertain = operation.Deliveries
               .Any(item => item.MessageId is null);

            var delivered = operation.Deliveries
               .Count(item => item.MessageId.HasValue);

            var refused       = exception is BotCommandException;
            var uncertainPost = toStaff && conversation is { CreationStarted: true, ThreadId: null };

            if ((uncertain || uncertainPost) && !refused)
                operation.State = "Uncertain";
            else if (delivered > 0)
                operation.State = "Partial";
            else
                operation.State = refused ? "Refused" : "Unpublished";

            operation.Detail = operation.State switch
            {
                "Uncertain" when uncertainPost => "Saved but message unpublished: post creation is uncertain. The saved attempt will be reconciled before any new post; no automatic resend.",
                "Uncertain"                    => $"Uncertain delivery ({delivered} confirmed parts). Further content was stopped; no automatic resend.",
                "Partial" when refused         => $"Partial delivery ({delivered} confirmed parts). {exception.Message}",
                "Partial"                      => $"Partial delivery ({delivered} confirmed parts); remaining content was not sent.",
                "Refused"                      => exception.Message,
                _                              => "Saved but unpublished: delivery could not start. No automatic resend.",
            };

            // Use the original token only for effects; preserve accountability during shutdown if the DB is reachable.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        if (conversation is null && operation.ConversationId.HasValue)
        {
            conversation = await db.SylvanMailConversations
               .SingleOrDefaultAsync(item => item.Id == operation.ConversationId, cancellationToken: ct);
        }

        await ReportAsync(db: db, message: message, threadId: conversation?.ThreadId, op: operation);

        if (toStaff && string.Equals(a: operation.State, "Delivered", comparisonType: StringComparison.Ordinal) && conversation?.ThreadId == previousThreadId && previousThreadId.HasValue)
            await discord.PingAfterGapAsync(settings: settings, threadId: previousThreadId.Value, message: message, ct: ct);
    }

    private async Task<SylvanMailConversation> ResolveConversationAsync(
        BotDbContext db,
        SylvanMailSettings settings,
        SylvanMailMessage message,
        SylvanMailOperation operation,
        CancellationToken ct)
    {
        var conversation = await db.SylvanMailConversations
           .SingleOrDefaultAsync(
                item => item.GuildId == settings.GuildId
                    && item.ForumId == settings.ForumId
                    && item.UserId == message.AuthorId
                    && !item.Retired,
                cancellationToken: ct);

        if (conversation?.ThreadId is not null && !await discord.OpenPostAsync(conversation: conversation, ct: ct))
        {
            conversation.Retired = true;
            await db.SaveChangesAsync(ct);
            conversation = null;
        }

        if (conversation is null)
        {
            conversation = new SylvanMailConversation
            {
                GuildId = settings.GuildId, ForumId = settings.ForumId, UserId = message.AuthorId,
            };

            db.SylvanMailConversations.Add(conversation);
        }

        operation.ConversationId = conversation.Id;
        await db.SaveChangesAsync(ct);

        if (conversation.ThreadId is null)
        {
            if (conversation.CreationStarted)
            {
                conversation.ThreadId = await discord.FindPostAsync(conversation: conversation, ct: ct);

                if (conversation.ThreadId is null)
                {
                    throw new BotCommandException(
                        "An earlier post creation has an uncertain outcome. No replacement was created; staff must reconcile the saved attempt.");
                }
            }
            else
            {
                conversation.CreationStarted = true;
                await db.SaveChangesAsync(ct);

                try
                {
                    conversation.ThreadId = await discord.CreatePostAsync(conversation: conversation, roleId: settings.RoleId, username: message.Username, ct: ct);
                }
                catch (BotCommandException)
                {
                    // A definite refusal made no post. Only ambiguous requests require marker reconciliation.
                    conversation.CreationStarted = false;
                    await db.SaveChangesAsync(CancellationToken.None);

                    throw;
                }
            }

            await db.SaveChangesAsync(ct);

            if (!await discord.OpenPostAsync(conversation: conversation, ct: ct))
                throw new BotCommandException("The post disappeared before relay. This message was not sent.");
        }

        return conversation;
    }

    private async Task ReportAsync(BotDbContext db, SylvanMailMessage message, ulong? threadId, SylvanMailOperation op)
    {
        // Outcome notices still get a bounded attempt when the delivery token has timed out.
        using var noticeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var       noticeToken   = noticeTimeout.Token;

        if (string.Equals(a: op.State, "Delivered", comparisonType: StringComparison.Ordinal))
        {
            if (!op.ToStaff)
            {
                await discord.MarkDeliveredAsync(message: message, ct: noticeToken);

                return;
            }

            if (op.AcknowledgementStarted)
                return;

            var previouslyAcknowledged = await db.SylvanMailOperations
               .AsNoTracking()
               .AnyAsync(
                    item => item.GuildId == op.GuildId
                        && item.ConversationId == op.ConversationId
                        && item.ToStaff
                        && item.AcknowledgementMessageId != null,
                    cancellationToken: noticeToken);

            op.AcknowledgementStarted = true;
            await db.SaveChangesAsync(noticeToken);
            op.AcknowledgementMessageId = await discord.AcknowledgeAsync(source: message, first: !previouslyAcknowledged, ct: noticeToken);
            await db.SaveChangesAsync(noticeToken);

            return;
        }

        op.ReceiptMessageId = await discord.ReportAsync(source: message, threadId: threadId, operation: op, detail: op.Detail, ct: noticeToken);
        await db.SaveChangesAsync(noticeToken);
    }
}