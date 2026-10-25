namespace SylvanBot.Application.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using Common;
using Infrastructure.Data;

public sealed partial class TicketWorkflow
{
    public async Task<TicketChange> SetAsync(
        TicketActor actor,
        long number,
        string? duration,
        string? comment,
        bool? hidden,
        CancellationToken cancellationToken)
    {
        RequireStaff(actor);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: false, cancellationToken)
                     ?? throw new BotCommandException("No matching ticket in this server.");

        if (ticket.Action == TicketAction.Note)
            return await SetNoteAsync(actor, number, duration, comment, hidden, cancellationToken: cancellationToken);

        if (hidden is not null)
            throw new BotCommandException(
                "Changing moderation-ticket visibility is not supported. Nothing was changed.");

        return await SetModerationAsync(
            actor,
            number,
            TicketDetails.ParseFields(duration, comment),
            reply: null,
            cancellationToken);
    }

    public async Task<TicketChange> SetModerationAsync(
        TicketActor actor,
        long number,
        TicketDetails details,
        TicketReply? reply,
        CancellationToken cancellationToken)
    {
        // DM permissions have no interaction context and cached membership is not authoritative.
        actor = await discord.GetActorAsync(actor.GuildId, actor.UserId, cancellationToken);
        var auditCurrent = false;

        // Audit ingestion takes the publication lock itself. Reconcile before entering
        // the edit, then reload and validate the ticket and any delivered prompt below.
        await using (var readContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var current = await FindAsync(readContext, actor.GuildId, number, tracking: false, cancellationToken)
                          ?? throw new BotCommandException("No matching ticket in this server.");

            RequireModerationCorrection(actor, current);

            DateTimeOffset? requestedExpiry =
                details.HasDuration ? details.Expiry(current.CreatedAtUtc!.Value) : current.ExpiresAtUtc;

            var dueBan = current.Action == TicketAction.Ban && IsActive(current)
                                                            && requestedExpiry <= DateTimeOffset.UtcNow;

            if (dueBan)
                auditCurrent = await ReconcileAuditAsync(actor.GuildId, cancellationToken);
        }

        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: true, cancellationToken)
                         ?? throw new BotCommandException("No matching ticket in this server.");

            RequireModerationCorrection(actor, ticket);
            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);
            await ValidateReplyAsync(dbContext, actor, ticket, reply, cancellationToken);

            if (!details.HasDuration && details.Comment is null)
                return new TicketChange(ticket, NoChange: true);

            if (details.Comment is not null && (string.IsNullOrWhiteSpace(details.Comment)
                                                || details.Comment.Length > Ticket.CommentLimit))
                throw new BotCommandException(
                    "Provide a nonblank comment within the ticket comment limit. Nothing was changed.");

            DateTimeOffset? expiry = details.Expiry(ticket.CreatedAtUtc!.Value);

            var naturallyEnded = ticket.Action == TicketAction.Timeout
                                 && IsActive(ticket)
                                 && ticket.ExpiresAtUtc <= DateTimeOffset.UtcNow;

            if (naturallyEnded)
            {
                DateTimeOffset? liveExpiry = ticket.SubjectUserId is null
                    ? null
                    : await discord.GetTimeoutAsync(ticket.GuildId, ticket.SubjectUserId.Value, cancellationToken);

                if (liveExpiry > DateTimeOffset.UtcNow)
                    throw new BotCommandException(
                        "A newer live timeout is awaiting audit reconciliation. Nothing was changed.");

                ticket.Status = TicketStatus.Expired;
            }

            var changesLiveTimeout = details.HasDuration
                                     && ticket.Action == TicketAction.Timeout
                                     && IsActive(ticket)
                                     && ticket.ExpiresAtUtc > DateTimeOffset.UtcNow;

            if (ticket.Action == TicketAction.Timeout && details.HasDuration && details.DurationSeconds is null)
                throw new BotCommandException("Discord timeouts cannot be permanent. Nothing was changed.");

            if (changesLiveTimeout)
                expiry = details.TimeoutExpiry(ticket.CreatedAtUtc.Value, DateTimeOffset.UtcNow);

            if (details is { HasDuration: true, DurationSeconds: <= 0 })
                throw new BotCommandException("The duration must be positive. Nothing was changed.");

            if (changesLiveTimeout)
            {
                if (ticket.SubjectUserId is null)
                    throw new BotCommandException("The subject is unknown. No timeout change was attempted.");

                await discord.CheckCaptureAsync(ticket.GuildId, cancellationToken);

                DateTimeOffset? observedExpiry = await discord.GetTimeoutAsync(
                    ticket.GuildId,
                    ticket.SubjectUserId!.Value,
                    cancellationToken);

                if (observedExpiry != ticket.ExpiresAtUtc)
                    throw new BotCommandException(
                        "The live timeout changed. Wait for audit reconciliation and read the ticket again. Nothing was changed.");

                ticket.PrepareOperation(expiry, actor.UserId);
            }
            else if (details.HasDuration)
            {
                if (ticket.PendingOperationId is not null)
                {
                    // Supplying duration again explicitly replaces a stalled schedule. The worker
                    // reconciles the guild before making any further unban attempt.
                    ticket.ClearOperation();
                }

                ticket.DurationKnown = true;
                ticket.DurationSeconds = details.DurationSeconds;
                ticket.ExpiresAtUtc = expiry;
                ticket.ExpirySuspended = false;
            }

            if (details.Comment is not null)
                ticket.Comment = details.Comment;

            ticket.LastModifiedByUserId = actor.UserId;
            ticket.LastModifiedAtUtc = DateTimeOffset.UtcNow;
            ticket.Version++;
            ticket.UpdateCompletion();

            if (reply is not null)
            {
                var moderator = await dbContext.TicketModerators
                    .SingleAsync(moderator => moderator.UserId == actor.UserId, cancellationToken);

                moderator.LastReadMessageId = reply.MessageId;
                moderator.Version++;
            }

            await SaveModerationAsync(dbContext, ticket, cancellationToken);
            string? outcome = null;

            if (changesLiveTimeout)
            {
                outcome = await ApplyOperationAsync(dbContext, ticket, cancellationToken);
            }
            else if (ticket.Action == TicketAction.Ban
                     && IsActive(ticket)
                     && ticket.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                try
                {
                    outcome = await RecoverModerationAsync(dbContext, ticket, auditCurrent, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogFailure(ticket, "reconciling due ban expiry", exception);

                    outcome =
                        "The details were saved, but ban expiry could not be confirmed. Recovery will reconsider it on a later pass.";
                }
            }

            var change = await PublishObservedAsync(dbContext, ticket, cancellationToken);

            return change with { ModerationOutcome = outcome };
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    private static void RequireModerationCorrection(TicketActor actor, Ticket ticket)
    {
        if (ticket.SourceAuditId is null || ticket.Action is not (TicketAction.Timeout or TicketAction.Ban))
            throw new BotCommandException("This ticket type is read-only.");

        if (!ticket.CanCorrect(actor))
            throw new BotCommandException(
                "You need the matching moderation permission to correct your own ticket, or View Audit Log/Administrator to correct another moderator's ticket.");
    }

    private static async Task ValidateReplyAsync(
        BotDbContext dbContext,
        TicketActor actor,
        Ticket ticket,
        TicketReply? reply,
        CancellationToken cancellationToken)
    {
        if (reply is null)
            return;

        var moderator = await dbContext.TicketModerators
            .SingleOrDefaultAsync(moderator => moderator.UserId == actor.UserId, cancellationToken);

        var current = moderator is not null
                      && moderator.ActiveTicketId == ticket.Id
                      && moderator.ChannelId == reply.ChannelId
                      && moderator.PromptMessageId is not null
                      && reply.MessageId > moderator.PromptMessageId
                      && (reply.PromptMessageId is null || moderator.PromptMessageId == reply.PromptMessageId)
                      && moderator.PromptVersion == ticket.Version
                      && reply.TicketVersion == ticket.Version
                      && (moderator.LastReadMessageId is null || reply.MessageId > moderator.LastReadMessageId)
                      && ticket.ModeratorUserId == actor.UserId
                      && ticket.Completion == TicketCompletion.Pending;

        if (!current)
            throw new BotCommandException(
                "That prompt is no longer current. Reply to the latest prompt; this answer was not applied to another ticket.");
    }

    private async Task SaveModerationAsync(BotDbContext dbContext, Ticket ticket, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BotCommandException(
                "This ticket changed concurrently. Read it again before retrying.",
                exception);
        }
        catch (Exception exception)
        {
            LogFailure(ticket, "saving moderation details", exception);

            throw new BotCommandException(
                $"Saving ticket #{ticket.Number} could not be confirmed. No further Discord operation was attempted. Read the ticket before retrying.",
                exception);
        }
    }

    private async Task<string> ApplyOperationAsync(
        BotDbContext dbContext,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        var operationId = ticket.PendingOperationId!.Value;
        ticket.PendingOperationAttempted = true;

        // Persist before the network call. Recovery reconciles audit and live state before retrying.
        await SaveModerationAsync(dbContext, ticket, cancellationToken);

        try
        {
            if (ticket.Action == TicketAction.Timeout)
            {
                DateTimeOffset? expiry =
                    ticket.PendingExpiryUtc > DateTimeOffset.UtcNow ? ticket.PendingExpiryUtc : null;

                await discord.SetTimeoutAsync(ticket, expiry, operationId, cancellationToken);

                if (ticket.PendingExpiryUtc is not null)
                    ticket.SetObservedExpiry(ticket.PendingExpiryUtc.Value);

                if (expiry is null)
                    ticket.Status = TicketStatus.Expired;
            }
            else
            {
                await discord.UnbanAsync(ticket, operationId, cancellationToken);
                ticket.Status = TicketStatus.Expired;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BotCommandException exception)
        {
            LogFailure(ticket, "moderation request rejected", exception);

            ticket.RejectOperation();
            await SaveModerationAsync(dbContext, ticket, cancellationToken);

            return ticket.Action == TicketAction.Timeout
                ? "Discord rejected the timeout change. The existing timeout and its duration were retained; any supplied comment was saved."
                : "The details were saved, but Discord rejected the scheduled unban. Expiry will retry after reconciliation.";
        }
        catch (Exception exception)
        {
            LogFailure(ticket, "applying moderation change", exception);

            if (ticket.Action == TicketAction.Ban)
                ticket.Status = TicketStatus.ExpirationFailed;

            ticket.Version++;
            ticket.UpdateCompletion();
            await SaveModerationAsync(dbContext, ticket, cancellationToken);

            return
                "The details were saved, but the Discord change could not be confirmed. It will be reconciled; do not repeat it blindly.";
        }

        ticket.Version++;
        ticket.UpdateCompletion();
        await SaveModerationAsync(dbContext, ticket, cancellationToken);

        return ticket.Status == TicketStatus.Expired
            ? "Discord confirmed that the action expired; audit reconciliation is pending."
            : "Discord confirmed the live timeout update; audit reconciliation is pending.";
    }

    private async Task<TicketChange> PublishObservedAsync(
        BotDbContext dbContext,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        if (ticket.Hidden)
            return new TicketChange(ticket);

        var channelId = ticket.ChannelId ?? await dbContext.TicketConfigurations
            .Where(configuration => configuration.GuildId == ticket.GuildId)
            .Select(configuration => configuration.DestinationChannelId)
            .SingleOrDefaultAsync(cancellationToken);

        if (channelId is null)
            return new TicketChange(ticket, PublicationUncertain: true);

        try
        {
            await discord.CheckPublicationAsync(ticket.GuildId, channelId.Value, cancellationToken);

            var change = await PublishCorrectionAsync(
                dbContext,
                ticket,
                channelId.Value,
                unhiding: false,
                cancellationToken);

            if (!change.PublicationUncertain && change.ReferenceSaved && change.ConfirmedPublication is not null)
            {
                ticket.PublishedVersion = ticket.Version;
                await SaveModerationAsync(dbContext, ticket, cancellationToken);
            }

            return change;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(ticket, "publishing observed action", exception);

            return new TicketChange(ticket, PublicationUncertain: true);
        }
    }
}
