using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailWorkflow(
    IDbContextFactory<BotDbContext> contexts,
    ISylvanMailDiscord discord,
    ISylvanMailForum forum,
    SylvanMailReporting reporting,
    SylvanMailConversations conversations
)
{
    public async Task RelayAsync(SylvanMailMessage message, CancellationToken ct)
    {
        // Outbound delivery is reachable only through author confirmation and a fresh draft read.
        if (message.GuildId is not null)
            return;

        await using var db = await contexts.CreateDbContextAsync(ct);
        await SylvanMailSerialisation.EnterAsync(db: db, ct: ct);

        try
        {
            await RelayLockedAsync(db: db, message: message, ct: ct);
        }
        finally
        {
            await SylvanMailSerialisation.ExitAsync(db);
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
            await ReportUnavailableAsync(message, ct);

            return;
        }

        var destination = settings[0];
        var toStaff = message.GuildId is null;

        IQueryable<SylvanMailConversation> conversationQuery = db.SylvanMailConversations
            .AsNoTracking()
            .Where(item => item.GuildId == destination.GuildId && item.ForumId == destination.ForumId && !item.Retired);

        var conversation = toStaff
            ? await conversationQuery
                .SingleOrDefaultAsync(item => item.UserId == message.AuthorId, cancellationToken: ct)
            : await conversationQuery
                .SingleOrDefaultAsync(item => item.ThreadId == message.SourceChannelId, cancellationToken: ct);

        if (!toStaff)
        {
            var mappedCase = message.GuildId == destination.GuildId && conversation is not null;

            if (!mappedCase)
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
            AuthorId = message.AuthorId,
            GuildId = destination.GuildId,
            ConversationId = conversation?.Id,
            ToStaff = toStaff,
            State = "Refused",
            Detail = "Not relayed: SylvanMail is temporarily busy. This message was refused; please send it again later.",
        };

        db.SylvanMailOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        await reporting.ReportAsync(db: db, message: message, threadId: conversation?.ThreadId, op: operation);
    }

    internal async Task RelayLockedAsync(BotDbContext db, SylvanMailMessage message, CancellationToken ct)
    {
        SylvanMailSettings[] destinations = await db.SylvanMailSettings
            .AsNoTracking()
            .Take(2)
            .ToArrayAsync(ct);

        if (destinations.Length != 1)
        {
            await ReportUnavailableAsync(message, ct);

            return;
        }

        var settings = destinations[0];
        var toStaff = message.GuildId is null;

        if (!toStaff && message.GuildId != settings.GuildId)
            return;

        var conversation = await FindConversationAsync(db, settings, message, ct);

        if (!toStaff && conversation is null)
            return;

        var operation = await db.SylvanMailOperations
            .Include(item => item.Deliveries)
            .SingleOrDefaultAsync(item => item.SourceMessageId == message.SourceMessageId, cancellationToken: ct);

        if (operation is not null)
        {
            await ReportPreviousAsync(db, message, operation, ct);

            return;
        }

        var previousThreadId = conversation?.ThreadId;

        operation = await SaveAttemptAsync(db, settings.GuildId, conversation?.Id, message, ct);

        conversation = await DeliverAsync(db, settings, message, operation, conversation, ct);

        if (conversation is null && operation.ConversationId.HasValue)
        {
            conversation = await db.SylvanMailConversations
                .SingleOrDefaultAsync(item => item.Id == operation.ConversationId, cancellationToken: ct);
        }

        await reporting.ReportAsync(db: db, message: message, threadId: conversation?.ThreadId, op: operation);

        var deliveredToExistingPost =
            toStaff && string.Equals(operation.State, "Delivered", StringComparison.Ordinal) && conversation?.ThreadId == previousThreadId;

        if (deliveredToExistingPost && previousThreadId is { } existingThread)
            await discord.PingAfterGapAsync(guildId: settings.GuildId, roleId: settings.RoleId, threadId: existingThread, message: message, ct: ct);
    }

    private static async Task<SylvanMailOperation> SaveAttemptAsync(
        BotDbContext db,
        ulong guildId,
        Guid? conversationId,
        SylvanMailMessage message,
        CancellationToken ct)
    {
        var operation = new SylvanMailOperation
        {
            SourceMessageId = message.SourceMessageId,
            SourceChannelId = message.SourceChannelId,
            AuthorId = message.AuthorId,
            GuildId = guildId,
            ConversationId = conversationId,
            ToStaff = message.GuildId is null,
        };

        db.SylvanMailOperations.Add(operation);
        await db.SaveChangesAsync(ct);

        return operation;
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

    private static Task<SylvanMailConversation?> FindConversationAsync(
        BotDbContext db,
        SylvanMailSettings settings,
        SylvanMailMessage message,
        CancellationToken ct)
    {
        IQueryable<SylvanMailConversation> active = db.SylvanMailConversations
            .Where(item => item.GuildId == settings.GuildId && item.ForumId == settings.ForumId)
            .Where(item => !item.Retired);

        return message.GuildId is null
            ? active
                .SingleOrDefaultAsync(item => item.UserId == message.AuthorId, ct)
            : active
                .SingleOrDefaultAsync(item => item.ThreadId == message.SourceChannelId, ct);
    }

    private static async Task<SylvanMailConversation?> RecordDeliveryFailureAsync(
        BotDbContext db,
        SylvanMailOperation operation,
        SylvanMailConversation? conversation,
        Exception exception)
    {
        if (conversation is null && operation.ConversationId.HasValue)
        {
            conversation = await db.SylvanMailConversations
                .SingleOrDefaultAsync(
                    item => item.Id == operation.ConversationId,
                    cancellationToken: CancellationToken.None);
        }

        var confirmedParts = operation.Deliveries
            .Count(item => item.MessageId.HasValue);

        var uncertainDelivery = operation.Deliveries
            .Any(item => item.MessageId is null);

        var uncertainPost = operation.ToStaff && conversation is { CreationStarted: true, ThreadId: null };
        Optional<string> refusal = exception is BotCommandException ? new Optional<string>(exception.Message) : default;
        var outcome = SylvanMailDeliveryOutcome.FromFailure(confirmedParts, uncertainDelivery, uncertainPost, refusal);
        operation.State = outcome.State;
        operation.Detail = outcome.Detail;

        // Use the original token only for effects; preserve accountability during shutdown if the DB is reachable.
        await db.SaveChangesAsync(CancellationToken.None);

        return conversation;
    }

    private async Task ReportUnavailableAsync(SylvanMailMessage message, CancellationToken ct)
    {
        if (message.GuildId is not null)
            return;

        await discord.ReportAsync(
            message,
            default,
            default,
            "SylvanMail is unavailable; your message was not relayed.",
            ct);
    }

    private async Task PublishContentAsync(
        BotDbContext db,
        ulong guildId,
        SylvanMailMessage message,
        SylvanMailOperation operation,
        ulong destinationChannelId,
        ImmutableArray<byte[]> attachments,
        CancellationToken ct)
    {
        // An uncertain notice is not retried, but subsequent messages can still be delivered.
        var label = operation.ToStaff ? await discord.InboundPrefixAsync(guildId: guildId, message: message, ct: ct) : message.OutboundPrefix;
        ImmutableArray<string> chunks = SylvanMailContent.SplitContent(content: message.Content, 2000 - label.Length);
        var ordinal = 0;

        foreach (var chunk in chunks)
        {
            await PublishAsync(
                db: db,
                op: operation,
                ordinal: ordinal++,
                () => discord.SendTextAsync(channelId: destinationChannelId, label + chunk, ct: ct),
                ct: ct);
        }

        for (var index = 0; index < message.Attachments.Length; index++)
        {
            var attachment = message.Attachments[index];
            var bytes = attachments[index];

            await PublishAsync(
                db: db,
                op: operation,
                ordinal: ordinal++,
                () => discord.SendAttachmentAsync(channelId: destinationChannelId, attachment: attachment, bytes: bytes, label: label, ct: ct),
                ct: ct);
        }
    }

    private async Task ReportPreviousAsync(BotDbContext db, SylvanMailMessage message, SylvanMailOperation operation, CancellationToken ct)
    {
        var previousThread = await db.SylvanMailConversations
            .Where(item => item.Id == operation.ConversationId)
            .Select(item => item.ThreadId)
            .SingleOrDefaultAsync(ct);

        if (string.Equals(a: operation.State, "Sending", comparisonType: StringComparison.Ordinal))
        {
            operation.State = "Uncertain";
            operation.Detail = "Interrupted delivery: some content may have arrived. No automatic resend.";
            await db.SaveChangesAsync(ct);
        }

        if (string.Equals(a: operation.State, "Saved", comparisonType: StringComparison.Ordinal))
        {
            operation.State = "Unpublished";
            operation.Detail = "Interrupted before delivery started. No automatic resend.";
            await db.SaveChangesAsync(ct);
        }

        await reporting.ReportAsync(db: db, message: message, threadId: previousThread, op: operation);
    }

    private async Task<SylvanMailConversation?> DeliverAsync(
        BotDbContext db,
        SylvanMailSettings settings,
        SylvanMailMessage message,
        SylvanMailOperation operation,
        SylvanMailConversation? conversation,
        CancellationToken ct)
    {
        var toStaff = operation.ToStaff;

        try
        {
            // Discord already accepted outgoing drafts; only inbound delivery needs destination validation.
            if (toStaff)
                await forum.ValidateForumAsync(
                    guildId: settings.GuildId,
                    forumId: settings.ForumId,
                    roleId: settings.RoleId,
                    ct: ct,
                    validatePing: false);
            SylvanMailContent.Validate(message);

            // Download all attachments before publishing any of this message; retain at most 24 MiB of attachment payload.
            var attachments = ImmutableArray.CreateBuilder<byte[]>(message.Attachments.Length);

            foreach (var attachment in message.Attachments)
                attachments.Add(await discord.DownloadAsync(attachment: attachment, ct: ct));

            if (toStaff)
                conversation = await conversations.ResolveAsync(db: db, settings: settings, message: message, operation: operation, ct: ct);

            if (conversation is not { ThreadId: { } threadId })
                throw new BotCommandException("The mapped post is unavailable. This message was not sent.");

            operation.DestinationChannelId = toStaff ? threadId : await discord.OpenDmAsync(userId: conversation.UserId, ct: ct);
            await db.SaveChangesAsync(ct);

            if (toStaff)
                await SendFirstNoticeAsync(db, conversation, message.SourceChannelId, ct);

            await PublishContentAsync(db, settings.GuildId, message, operation, operation.DestinationChannelId.Value, attachments.ToImmutable(), ct);

            operation.State = "Delivered";
            operation.Detail = "Delivered: Discord accepted every part.";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            conversation = await RecordDeliveryFailureAsync(db, operation, conversation, exception);
        }

        return conversation;
    }

    private async Task SendFirstNoticeAsync(BotDbContext db, SylvanMailConversation conversation, ulong sourceChannelId, CancellationToken ct)
    {
        if (!conversation.NoticeStarted)
        {
            conversation.NoticeStarted = true;
            await db.SaveChangesAsync(ct);

            conversation.NoticeMessageId = await discord.SendTextAsync(
                channelId: sourceChannelId,
                "SylvanMail is in beta: bugs and lost messages are possible. Your DMs to Sylvan reach staff in a private forum post. Staff replies are sent by Sylvan after the author confirms them; edits and deletions after sending are not relayed.",
                ct: ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
