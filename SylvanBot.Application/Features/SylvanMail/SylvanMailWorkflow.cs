using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailWorkflow(
    IDbContextFactory<BotDbContext> contexts,
    ISylvanMailDiscord discord,
    ISylvanMailForum forum,
    SylvanMailReporting reporting,
    SylvanMailConversations conversations,
    ILogger<SylvanMailWorkflow> logger)
{
    public async Task RelayAsync(SylvanMailMessage message, CancellationToken ct)
    {
        // Outbound delivery is reachable only through author confirmation and a fresh draft read.
        if (message.GuildId is not null)
            return;

        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);
        await SylvanMailSerialisation.EnterConversationAsync(db, message.AuthorId, logger, ct);

        try
        {
            await RelayLockedAsync(db, message, ct);
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
            await ReportUnavailableAsync(message, ct);
            return;
        }

        SylvanMailSettings settings = destinations[0];
        var toStaff = message.GuildId is null;
        if (!toStaff && message.GuildId != settings.GuildId)
            return;

        SylvanMailConversation? conversation = await FindConversationAsync(db, settings, message, ct);
        if (!toStaff && conversation is null)
            return;

        SylvanMailOperation? previousAttempt = await db.SylvanMailOperations
            .SingleOrDefaultAsync(item => item.SourceMessageId == message.SourceMessageId, cancellationToken: ct);

        if (previousAttempt is not null)
        {
            var previousThread = await db.SylvanMailConversations
                .Where(item => item.Id == previousAttempt.ConversationId)
                .Select(item => item.ThreadId)
                .SingleOrDefaultAsync(ct);

            if (previousAttempt.RecoverInterruptedAttempt())
                await db.SaveChangesAsync(ct);

            await reporting.ReportAsync(db, message, previousThread, previousAttempt);
            return;
        }

        var previousThreadId = conversation?.ThreadId;
        SylvanMailOperation operation = new SylvanMailOperation
        {
            SourceMessageId = message.SourceMessageId,
            SourceChannelId = message.SourceChannelId,
            AuthorId = message.AuthorId,
            GuildId = settings.GuildId,
            ConversationId = conversation?.Id,
            ToStaff = toStaff,
        };

        db.SylvanMailOperations.Add(operation);
        await db.SaveChangesAsync(ct);

        conversation = await DeliverAsync(db, settings, message, operation, conversation, ct);

        if (conversation is null && operation.ConversationId.HasValue)
            conversation = await db.SylvanMailConversations
                .SingleOrDefaultAsync(item => item.Id == operation.ConversationId, cancellationToken: ct);

        var deliveredToStaff = toStaff && operation.State == SylvanMailDeliveryState.Delivered;
        if (deliveredToStaff && conversation is { ThreadId: { } newThread } && newThread != previousThreadId)
        {
            try
            {
                await discord.SendTicketListAsync(guildId: conversation.GuildId, threadId: newThread, userId: conversation.UserId, ct);
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

        await reporting.ReportAsync(db, message, conversation?.ThreadId, operation);

        var deliveredToExistingPost = deliveredToStaff && conversation?.ThreadId == previousThreadId;
        if (deliveredToExistingPost && previousThreadId is { } existingThread)
            await discord.PingAfterGapAsync(guildId: settings.GuildId, roleId: settings.RoleId, threadId: existingThread, message, ct);
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
                await forum.ValidateForumAsync(
                    guildId: settings.GuildId,
                    forumId: settings.ForumId,
                    roleId: settings.RoleId,
                    ct,
                    validatePing: false);

            SylvanMailContent.Validate(message);

            // Download all attachments before publishing any of this message; retain at most 24 MiB of attachment payload.
            NextStage("download_attachments");
            ImmutableArray<byte[]>.Builder attachments = ImmutableArray.CreateBuilder<byte[]>(message.Attachments.Length);

            foreach (SylvanMailAttachment attachment in message.Attachments)
                attachments.Add(await discord.DownloadAsync(attachment, ct));

            NextStage("resolve_conversation");
            if (toStaff)
                conversation = await conversations.ResolveAsync(db, settings, message, operation, ct);

            if (conversation is not { ThreadId: { } threadId })
                throw new BotCommandException("The mapped post is unavailable. This message was not sent.");

            NextStage("open_destination");
            operation.DestinationChannelId = toStaff ? threadId : await discord.OpenDmAsync(conversation.UserId, ct);
            await db.SaveChangesAsync(ct);

            NextStage("publish_content");
            await PublishContentAsync(
                db,
                settings.GuildId,
                message,
                operation,
                operation.DestinationChannelId.Value,
                attachments.ToImmutable(),
                ct);

            NextStage("save_outcome");
            operation.MarkDelivered();
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            NextStage("record_failure");

            if (conversation is null && operation.ConversationId.HasValue)
                conversation = await db.SylvanMailConversations
                    .SingleOrDefaultAsync(
                        item => item.Id == operation.ConversationId,
                        cancellationToken: CancellationToken.None);

            var uncertainPost = toStaff && conversation is { CreationStarted: true, ThreadId: null };
            Optional<string> refusal = exception is BotCommandException ? new Optional<string>(exception.Message) : default;
            operation.RecordDeliveryFailure(uncertainPost, refusal);

            // Attempt to save the failure outcome even during shutdown.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            NextStage("finished");
        }

        return conversation;
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
        SylvanMailDestination destination = new SylvanMailDestination(
            operation.ToStaff ? SylvanMailDestinationKind.StaffThread : SylvanMailDestinationKind.MemberDm,
            destinationChannelId);

        var label = operation.ToStaff ? await discord.InboundPrefixAsync(guildId, message, ct) : message.OutboundPrefix;
        ImmutableArray<string> chunks = SylvanMailContent.SplitContent(message.Content, 2000 - label.Length);
        var ordinal = 0;

        foreach (var chunk in chunks)
            await SendPartAsync(db, operation, ordinal++, () => discord.SendTextAsync(destination, label + chunk, ct), ct);

        for (var index = 0; index < message.Attachments.Length; index++)
        {
            SylvanMailAttachment attachment = message.Attachments[index];
            var bytes = attachments[index];

            await SendPartAsync(db, operation, ordinal++, () => discord.SendAttachmentAsync(destination, attachment, bytes, label, ct), ct);
        }
    }

    private async Task SendPartAsync(
        BotDbContext db,
        SylvanMailOperation operation,
        int ordinal,
        Func<Task<ulong>> send,
        CancellationToken ct)
    {
        discord.RequireReady(operation.ToStaff ? SylvanMailDestinationKind.StaffThread : SylvanMailDestinationKind.MemberDm);

        SylvanMailDelivery part = operation.StartSendingPart(ordinal);
        await db.SaveChangesAsync(ct);

        part.MessageId = await send();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Discord accepted the part. Without a saved ID, failure handling must count it as unconfirmed.
            part.MessageId = null;
            throw;
        }
    }

    private async Task RefuseAsync(SylvanMailMessage message, string detail, CancellationToken ct)
    {
        // Do not wait behind relay's Discord calls when the transient buffer is full.
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);

        var recorded = await db.SylvanMailOperations
            .AnyAsync(item => item.SourceMessageId == message.SourceMessageId, cancellationToken: ct);

        if (recorded)
            return;

        SylvanMailSettings[] settings = await db.SylvanMailSettings
            .AsNoTracking()
            .Take(2)
            .ToArrayAsync(ct);

        if (settings.Length != 1)
        {
            await ReportUnavailableAsync(message, ct);
            return;
        }

        SylvanMailSettings destination = settings[0];
        var toStaff = message.GuildId is null;
        IQueryable<SylvanMailConversation> conversationQuery = db.SylvanMailConversations
            .AsNoTracking()
            .Where(item => item.GuildId == destination.GuildId && item.ForumId == destination.ForumId && !item.Retired);

        SylvanMailConversation? conversation = toStaff
            ? await conversationQuery
                .SingleOrDefaultAsync(item => item.UserId == message.AuthorId, cancellationToken: ct)
            : await conversationQuery
                .SingleOrDefaultAsync(item => item.ThreadId == message.SourceChannelId, cancellationToken: ct);

        var mappedStaffPost = message.GuildId == destination.GuildId && conversation is not null;
        if (!toStaff && !mappedStaffPost)
            return;

        SylvanMailOperation operation = new SylvanMailOperation
        {
            SourceMessageId = message.SourceMessageId,
            SourceChannelId = message.SourceChannelId,
            AuthorId = message.AuthorId,
            GuildId = destination.GuildId,
            ConversationId = conversation?.Id,
            ToStaff = toStaff,
        };

        operation.MarkRefused(detail);
        db.SylvanMailOperations.Add(operation);
        await db.SaveChangesAsync(ct);

        await reporting.ReportAsync(db, message, conversation?.ThreadId, operation);
    }

    private async Task ReportUnavailableAsync(SylvanMailMessage message, CancellationToken ct)
    {
        if (message.GuildId is not null)
            return;

        await discord.ReportAsync(
            message,
            threadId: default,
            receiptId: default,
            detail: "SylvanMail is unavailable; your message was not relayed.",
            ct);
    }
}