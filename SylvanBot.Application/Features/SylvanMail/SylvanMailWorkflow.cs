using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailWorkflow(
    IDbContextFactory<BotDbContext> contexts,
    ISylvanMailDiscord discord,
    ISylvanMailForum forum,
    SylvanMailReporting reporting,
    SylvanMailConversations conversations,
    ILogger<SylvanMailWorkflow> logger
)
{
    public async Task RelayAsync(SylvanMailMessage message, CancellationToken ct)
    {
        // Outbound delivery is reachable only through author confirmation and a fresh draft read.
        if (message.GuildId is not null)
            return;

        await using var db = await contexts.CreateDbContextAsync(ct);
        await SylvanMailSerialisation.EnterConversationAsync(db, message.AuthorId, logger, ct);

        try
        {
            await RelayLockedAsync(db: db, message: message, ct: ct);
        }
        finally
        {
            await SylvanMailSerialisation.ExitConversationAsync(db, message.AuthorId);
        }
    }

    public Task RefuseBusyAsync(SylvanMailMessage message, CancellationToken ct) =>
        RefuseAsync(message, "Not relayed: SylvanMail is temporarily busy. This message was refused; please send it again later.", ct);

    public Task RefuseUnavailableAsync(SylvanMailMessage message, CancellationToken ct) =>
        RefuseAsync(message, "Not relayed: SylvanMail is disconnected or not ready. Please send a new DM after service recovers.", ct);

    internal async Task RelayLockedAsync(BotDbContext db, SylvanMailMessage message, CancellationToken ct)
    {
        SylvanMailSettings[] destinations = await db.SylvanMailSettings
           .AsNoTracking()
           .Take(2)
           .ToArrayAsync(ct);

        if (destinations.Length != 1)
        {
            await ReportUnavailableAsync(message: message, ct: ct);

            return;
        }

        var settings = destinations[0];
        var toStaff = message.GuildId is null;

        if (!toStaff && message.GuildId != settings.GuildId)
            return;

        var conversation = await FindConversationAsync(db: db, settings: settings, message: message, ct: ct);

        if (!toStaff && conversation is null)
            return;

        var operation = await db.SylvanMailOperations
           .Include(item => item.Deliveries)
           .SingleOrDefaultAsync(item => item.SourceMessageId == message.SourceMessageId, cancellationToken: ct);

        if (operation is not null)
        {
            await ReportPreviousAsync(db: db, message: message, operation: operation, ct: ct);

            return;
        }

        var previousThreadId = conversation?.ThreadId;

        operation = await SaveAttemptAsync(db: db, guildId: settings.GuildId, conversationId: conversation?.Id, message: message, ct: ct);

        conversation = await DeliverAsync(db: db, settings: settings, message: message, operation: operation, conversation: conversation, ct: ct);

        if (conversation is null && operation.ConversationId.HasValue)
        {
            conversation = await db.SylvanMailConversations
               .SingleOrDefaultAsync(item => item.Id == operation.ConversationId, cancellationToken: ct);
        }

        var deliveredToNewPost =
            toStaff
            && string.Equals(a: operation.State, "Delivered", comparisonType: StringComparison.Ordinal)
            && conversation?.ThreadId != previousThreadId;

        if (deliveredToNewPost && conversation is { ThreadId: { } newThread })
        {
            try
            {
                await discord.SendTicketListAsync(conversation.GuildId, newThread, conversation.UserId, ct);
            }
            catch (Exception exception)
            {
                // Ticket context is best effort and must not change the mail delivery outcome.
                logger.LogWarning(
                    exception,
                    "SylvanMail ticket lookup failed in guild {GuildId}, thread {ThreadId}, for user {UserId}",
                    conversation.GuildId,
                    newThread,
                    conversation.UserId);
            }
        }

        await reporting.ReportAsync(db: db, message: message, threadId: conversation?.ThreadId, op: operation);

        var deliveredToExistingPost =
            toStaff
            && string.Equals(a: operation.State, "Delivered", comparisonType: StringComparison.Ordinal)
            && conversation?.ThreadId == previousThreadId;

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
               .SingleOrDefaultAsync(item => item.UserId == message.AuthorId, cancellationToken: ct)
            : active
               .SingleOrDefaultAsync(item => item.ThreadId == message.SourceChannelId, cancellationToken: ct);
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

        var outcome = SylvanMailDeliveryOutcome.FromFailure(
            confirmedParts: confirmedParts,
            uncertainDelivery: uncertainDelivery,
            uncertainPost: uncertainPost,
            refusal: refusal);

        operation.State = outcome.State;
        operation.Detail = outcome.Detail;

        // Use the original token only for effects; preserve accountability during shutdown if the DB is reachable.
        await db.SaveChangesAsync(CancellationToken.None);

        return conversation;
    }

    private async Task RefuseAsync(SylvanMailMessage message, string detail, CancellationToken ct)
    {
        // Do not wait behind relay's Discord calls when the transient buffer is full.
        await using var db = await contexts.CreateDbContextAsync(ct);

        SylvanMailSettings[] settings = await db.SylvanMailSettings
           .AsNoTracking()
           .Take(2)
           .ToArrayAsync(ct);

        if (settings.Length != 1)
        {
            await ReportUnavailableAsync(message: message, ct: ct);

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
            Detail = detail,
        };

        db.SylvanMailOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        await reporting.ReportAsync(db: db, message: message, threadId: conversation?.ThreadId, op: operation);
    }

    private async Task PublishAsync(BotDbContext db, SylvanMailOperation op, int ordinal, Func<Task<ulong>> publish, CancellationToken ct)
    {
        discord.RequireReady(op.ToStaff ? SylvanMailDestinationKind.StaffThread : SylvanMailDestinationKind.MemberDm);

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

    private async Task ReportUnavailableAsync(SylvanMailMessage message, CancellationToken ct)
    {
        if (message.GuildId is not null)
            return;

        await discord.ReportAsync(
            source: message,
            threadId: default,
            receiptId: default,
            "SylvanMail is unavailable; your message was not relayed.",
            ct: ct);
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
        var destination = new SylvanMailDestination(
            operation.ToStaff ? SylvanMailDestinationKind.StaffThread : SylvanMailDestinationKind.MemberDm,
            destinationChannelId);

        var label = operation.ToStaff ? await discord.InboundPrefixAsync(guildId: guildId, message: message, ct: ct) : message.OutboundPrefix;
        ImmutableArray<string> chunks = SylvanMailContent.SplitContent(content: message.Content, 2000 - label.Length);
        var ordinal = 0;

        foreach (var chunk in chunks)
            await PublishAsync(
                db: db,
                op: operation,
                ordinal: ordinal++,
                () => discord.SendTextAsync(destination: destination, label + chunk, ct: ct),
                ct: ct);

        for (var index = 0; index < message.Attachments.Length; index++)
        {
            var attachment = message.Attachments[index];
            var bytes = attachments[index];

            await PublishAsync(
                db: db,
                op: operation,
                ordinal: ordinal++,
                () => discord.SendAttachmentAsync(destination: destination, attachment: attachment, bytes: bytes, label: label, ct: ct),
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
        var stage = "validate";
        var stageStarted = Stopwatch.GetTimestamp();

        void NextStage(string next)
        {
            logger.LogDebug(
                "SylvanMail delivery for source {SourceId}, stage {Stage}: {ElapsedMs} ms",
                message.SourceMessageId,
                stage,
                Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);

            stage = next;
            stageStarted = Stopwatch.GetTimestamp();
        }

        try
        {
            discord.RequireReady(toStaff ? SylvanMailDestinationKind.StaffThread : SylvanMailDestinationKind.MemberDm);

            // Discord already accepted outgoing drafts; only inbound delivery needs destination validation.
            if (toStaff)
            {
                await forum.ValidateForumAsync(
                    guildId: settings.GuildId,
                    forumId: settings.ForumId,
                    roleId: settings.RoleId,
                    ct: ct,
                    validatePing: false);
            }

            SylvanMailContent.Validate(message);

            // Download all attachments before publishing any of this message; retain at most 24 MiB of attachment payload.
            NextStage("download_attachments");
            var attachments = ImmutableArray.CreateBuilder<byte[]>(message.Attachments.Length);

            foreach (var attachment in message.Attachments)
                attachments.Add(await discord.DownloadAsync(attachment: attachment, ct: ct));

            NextStage("resolve_conversation");

            if (toStaff)
                conversation = await conversations.ResolveAsync(db: db, settings: settings, message: message, operation: operation, ct: ct);

            if (conversation is not { ThreadId: { } threadId })
                throw new BotCommandException("The mapped post is unavailable. This message was not sent.");

            NextStage("open_destination");
            operation.DestinationChannelId = toStaff ? threadId : await discord.OpenDmAsync(userId: conversation.UserId, ct: ct);
            await db.SaveChangesAsync(ct);

            NextStage("publish_content");

            await PublishContentAsync(
                db: db,
                guildId: settings.GuildId,
                message: message,
                operation: operation,
                destinationChannelId: operation.DestinationChannelId.Value,
                attachments.ToImmutable(),
                ct: ct);

            NextStage("save_outcome");
            operation.State = "Delivered";
            operation.Detail = "Delivered: Discord accepted every part.";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            NextStage("record_failure");
            conversation = await RecordDeliveryFailureAsync(db: db, operation: operation, conversation: conversation, exception: exception);
        }
        finally
        {
            NextStage("finished");
        }

        return conversation;
    }
}