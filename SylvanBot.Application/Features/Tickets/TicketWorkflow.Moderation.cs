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
            cancellationToken);
    }

    public async Task<TicketChange> SetModerationAsync(
        TicketActor actor,
        long number,
        TicketDetails details,
        CancellationToken cancellationToken,
        int? expectedVersion = null,
        bool append = false)
    {
        // DM permissions have no interaction context and cached membership is not authoritative.
        actor = await discord.GetActorAsync(actor.GuildId, actor.UserId, cancellationToken);
        var auditCurrent = await ReconcileCorrectionAsync(actor, number, details, cancellationToken);

        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: true, cancellationToken)
                ?? throw new BotCommandException("No matching ticket in this server.");

            RequireModerationCorrection(actor, ticket);
            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

            if (expectedVersion is not null && expectedVersion != ticket.Version)
                throw new BotCommandException("This ticket changed. Reopen the editor before saving.");

            if (append && details.Comment is not null && !string.IsNullOrEmpty(ticket.Comment))
                details = details with { Comment = ticket.Comment + "\n" + details.Comment };

            if (details.HasDuration && ticket.DurationKnown && details.DurationSeconds == ticket.DurationSeconds)
                details = details with { HasDuration = false };

            if (!details.HasDuration && (details.Comment is null || string.Equals(details.Comment, ticket.Comment, StringComparison.Ordinal)))
                return new TicketChange(ticket, NoChange: true);

            if (ticket.HidePending)
                throw new BotCommandException("A hide request is being reconciled. Wait before editing.");

            var previousComment = ticket.Comment;
            var previousDuration = ticket.DurationSeconds;
            var durationWasKnown = ticket.DurationKnown;
            var changesLiveTimeout = await PrepareDetailsAsync(ticket, actor, details, cancellationToken);

            if (!string.Equals(previousComment, ticket.Comment, StringComparison.Ordinal))
                ticket.RecordModification(actor.UserId, append ? "appended comment" : "set comment", DateTimeOffset.UtcNow);

            await SaveModerationAsync(dbContext, ticket, cancellationToken);
            var outcome = await ApplyCorrectionAsync(dbContext, ticket, changesLiveTimeout, auditCurrent, cancellationToken);

            if (ticket.DurationKnown != durationWasKnown || ticket.DurationSeconds != previousDuration)
            {
                ticket.RecordDurationChange(actor.UserId);
                await SaveModerationAsync(dbContext, ticket, cancellationToken);
            }

            return (await PublishObservedAsync(dbContext, ticket, cancellationToken)) with { ModerationOutcome = outcome };
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    private static void RequireModerationCorrection(TicketActor actor, Ticket ticket)
    {
        if (ticket.Action is not (TicketAction.Timeout or TicketAction.Ban or TicketAction.VoiceMute)
            && actor is { IsAdministrator: false, CanViewAuditLog: false })
            throw new BotCommandException("This ticket type is read-only.");

        if (!ticket.CanCorrect(actor))
            throw new BotCommandException(
                "You need the matching moderation permission to correct your own ticket, or View Audit Log/Administrator to correct another moderator's ticket.");
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
                $"Saving ticket #{ticket.Number} could not be confirmed. No further operation was attempted. Read the ticket before retrying.",
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
            await SendOperationAsync(ticket, operationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BotCommandException exception)
        {
            LogFailure(ticket, "moderation request rejected", exception);

            ticket.HidePending = false;
            ticket.RejectOperation();
            await SaveModerationAsync(dbContext, ticket, cancellationToken);

            return exception.Message;
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

        return "Ticket updated.";
    }

    private async Task<TicketChange> PublishObservedAsync(
        BotDbContext dbContext,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        if (ticket.Hidden)
            return new TicketChange(ticket);

        var channelId = ticket.ChannelId
            ?? await dbContext.TicketConfigurations
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

            if (change is not { PublicationUncertain: false, ReferenceSaved: true, ConfirmedPublication: not null })
                return change;

            ticket.PublishedVersion = ticket.Version;
            await SaveModerationAsync(dbContext, ticket, cancellationToken);

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

    private async Task<bool> ReconcileCorrectionAsync(TicketActor actor, long number, TicketDetails details, CancellationToken cancellationToken)
    {
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

            var dueBan = current.Action == TicketAction.Ban
                && IsActive(current)
                && requestedExpiry <= DateTimeOffset.UtcNow;

            if (dueBan)
                auditCurrent = await ReconcileAuditAsync(actor.GuildId, cancellationToken);
        }

        return auditCurrent;
    }

    private async Task<bool> PrepareDetailsAsync(Ticket ticket, TicketActor actor, TicketDetails details, CancellationToken cancellationToken)
    {
        if (details.Comment is not null
            && (string.IsNullOrWhiteSpace(details.Comment)
                || details.Comment.Length > Ticket.CommentLimit))
            throw new BotCommandException(
                "Provide a nonblank comment within the ticket comment limit. Nothing was changed.");

        if (details.HasDuration && ticket.Action is not (TicketAction.Timeout or TicketAction.Ban or TicketAction.VoiceMute))
            throw new BotCommandException("This ticket type does not support changing duration.");

        DateTimeOffset? expiry = details.Expiry(ticket.CreatedAtUtc ?? DateTimeOffset.UtcNow);

        await ReconcileEndedTimeoutAsync(ticket, cancellationToken);

        var changesLiveTimeout = details.HasDuration
            && ticket.Action == TicketAction.Timeout
            && IsActive(ticket)
            && ticket.ExpiresAtUtc > DateTimeOffset.UtcNow;

        if (ticket.Action == TicketAction.Timeout && details.HasDuration && details.DurationSeconds is null)
            throw new BotCommandException("Discord timeouts cannot be permanent. Nothing was changed.");

        if (changesLiveTimeout)
            expiry = details.TimeoutExpiry(ticket.CreatedAtUtc ?? throw new BotCommandException("The original action time is unknown."), DateTimeOffset.UtcNow);

        if (details is { HasDuration: true, DurationSeconds: <= 0 })
            throw new BotCommandException("The duration must be positive. Nothing was changed.");

        await SetCorrectionDurationAsync(ticket, actor, details, changesLiveTimeout, expiry, cancellationToken);

        if (details.Comment is not null)
            ticket.Comment = details.Comment;

        ticket.LastModifiedByUserId = actor.UserId;
        ticket.LastModifiedAtUtc = DateTimeOffset.UtcNow;
        ticket.Version++;
        ticket.UpdateCompletion();

        return changesLiveTimeout;
    }

    private async Task<string?> ApplyCorrectionAsync(BotDbContext dbContext, Ticket ticket, bool changesLiveTimeout, bool auditCurrent, CancellationToken cancellationToken)
    {
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

        return outcome;
    }

    private async Task ReconcileEndedTimeoutAsync(Ticket ticket, CancellationToken cancellationToken)
    {
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
    }

    private async Task SetCorrectionDurationAsync(Ticket ticket, TicketActor actor, TicketDetails details, bool changesLiveTimeout, DateTimeOffset? expiry, CancellationToken cancellationToken)
    {
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

            if (ticket.DurationSeconds != details.DurationSeconds || !ticket.DurationKnown)
                ticket.ClearApproval();

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
    }

    private async Task SendOperationAsync(Ticket ticket, Guid operationId, CancellationToken cancellationToken)
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
}