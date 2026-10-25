namespace SylvanBot.Application.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Common;
using Infrastructure.Data;

public sealed partial class TicketWorkflow
{
    public async Task RecoverGuildAsync(ulong guildId, bool auditCurrent, CancellationToken cancellationToken)
    {
        long afterId = 0;

        while (true)
        {
            await using var readContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var enabled = await readContext.TicketConfigurations
                .AnyAsync(configuration => configuration.GuildId == guildId && configuration.Enabled
                                           && configuration.DestinationChannelId != null, cancellationToken);

            if (!enabled)
                return;

            var now = DateTimeOffset.UtcNow;

            List<long> ids = await readContext.Tickets
                .Where(ticket => ticket.GuildId == guildId && ticket.SourceAuditId != null && ticket.Id > afterId
                                 && (ticket.PendingOperationId != null
                                     || (!ticket.ExpirySuspended && (ticket.Status == TicketStatus.Active || ticket.Status == TicketStatus.ExpirationFailed)
                                         && ticket.ExpiresAtUtc <= now)
                                     || (!ticket.Hidden && !ticket.PublicationPending
                                         && (ticket.PublishedVersion == null || ticket.PublishedVersion < ticket.Version))))
                .OrderBy(ticket => ticket.Id)
                .Select(ticket => ticket.Id)
                .Take(100)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
                return;

            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await RecoverTicketAsync(guildId, id, auditCurrent, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError("Recovery failed for ticket record {TicketId} in guild {GuildId} ({FailureType})",
                        id, guildId, exception.GetType().Name);
                }
            }

            afterId = ids[^1];
        }
    }

    public async Task SuspendBanExpiryAsync(ulong guildId, CancellationToken cancellationToken)
    {
        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            await dbContext.Tickets
                .Where(ticket => ticket.GuildId == guildId && ticket.SourceAuditId != null && ticket.Action == TicketAction.Ban
                                 && !ticket.ExpirySuspended
                                 && (ticket.Status == TicketStatus.Active || ticket.Status == TicketStatus.ExpirationFailed))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ticket => ticket.ExpirySuspended, true)
                    .SetProperty(ticket => ticket.PendingOperationId, (Guid?)null)
                    .SetProperty(ticket => ticket.PendingRequestedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(ticket => ticket.PendingExpiryUtc, (DateTimeOffset?)null)
                    .SetProperty(ticket => ticket.PendingEditorUserId, (ulong?)null)
                    .SetProperty(ticket => ticket.PendingOperationAttempted, false)
                    .SetProperty(ticket => ticket.Version, ticket => ticket.Version + 1), cancellationToken);
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    public async Task RedeliverAsync(TicketActor actor, CancellationToken cancellationToken)
    {
        actor = await discord.GetActorAsync(actor.GuildId, actor.UserId, cancellationToken);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        await RequireEnabledAsync(dbContext, actor.GuildId, cancellationToken);

        var head = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.ModeratorUserId == actor.UserId && ticket.Completion == TicketCompletion.Pending
                             && dbContext.TicketConfigurations
                                 .Any(configuration => configuration.GuildId == ticket.GuildId && configuration.Enabled
                                                       && configuration.DestinationChannelId != null))
            .OrderBy(ticket => ticket.CreatedAtUtc)
            .ThenBy(ticket => ticket.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (head is null)
            throw new BotCommandException("Your ticket queue is empty.");

        if (head.GuildId != actor.GuildId)
            throw new BotCommandException("Your oldest outstanding ticket is in another server. Request redelivery there.");

        RequireModerationCorrection(actor, head);
        await CheckReadAsync(dbContext, actor, head, cancellationToken);

        var moderator = await dbContext.TicketModerators
            .SingleOrDefaultAsync(moderator => moderator.UserId == actor.UserId, cancellationToken);

        if (moderator is null)
        {
            moderator = new TicketModerator { UserId = actor.UserId };
            dbContext.TicketModerators.Add(moderator);
        }

        moderator.NextDeliveryUtc = null;
        moderator.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecoverTicketAsync(ulong guildId, long id, bool auditCurrent, CancellationToken cancellationToken)
    {
        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var enabled = await dbContext.TicketConfigurations
                .AnyAsync(configuration => configuration.GuildId == guildId && configuration.Enabled
                                           && configuration.DestinationChannelId != null, cancellationToken);

            if (!enabled)
                return;

            var ticket = await dbContext.Tickets
                .SingleAsync(ticket => ticket.GuildId == guildId && ticket.Id == id, cancellationToken);

            await RecoverModerationAsync(dbContext, ticket, auditCurrent, cancellationToken);

            var needsPublication = !ticket.PublicationPending
                                   && (ticket.PublishedVersion is null || ticket.PublishedVersion < ticket.Version);

            if (needsPublication)
                await PublishObservedAsync(dbContext, ticket, cancellationToken);
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    // Both completion and the worker enter here with the publication lock already held.
    private async Task<string?> RecoverModerationAsync(
        BotDbContext dbContext,
        Ticket ticket,
        bool auditCurrent,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        // A disable/re-enable between the scan and this operation invalidates its coverage.
        auditCurrent = auditCurrent && await dbContext.TicketConfigurations
            .AnyAsync(configuration => configuration.GuildId == ticket.GuildId && configuration.Enabled
                                       && configuration.CaptureFromUtc != null
                                       && configuration.LastAuditScanAtUtc >= configuration.CaptureFromUtc, cancellationToken);

        var superseded = ticket.Status == TicketStatus.Reverted || ticket.SupersededByNumber is not null;

        if (ticket.PendingOperationId is not null && superseded)
        {
            ticket.ClearOperation();
            ticket.Version++;
            ticket.UpdateCompletion();
            await SaveModerationAsync(dbContext, ticket, cancellationToken);
            return "The request was cancelled because the original action was superseded or reverted.";
        }

        if (!auditCurrent || ticket.SubjectUserId is null || ticket.ExpirySuspended)
            return "The details were saved, but expiry could not be reconciled. Recovery will reconsider it on a later pass.";

        if (ticket.PendingOperationId is not null)
        {
            DateTimeOffset? liveExpiry = null;
            bool confirmed;
            bool originalActionUnchanged;

            if (ticket.Action == TicketAction.Timeout)
            {
                liveExpiry = await discord.GetTimeoutAsync(ticket.GuildId, ticket.SubjectUserId.Value, cancellationToken);
                confirmed = ticket.PendingExpiryUtc > now
                    ? liveExpiry == ticket.PendingExpiryUtc
                    : liveExpiry is null || liveExpiry <= now;
                originalActionUnchanged = IsActive(ticket) && ticket.ExpiresAtUtc > now && liveExpiry == ticket.ExpiresAtUtc;
            }
            else
            {
                confirmed = !await discord.IsBannedAsync(ticket.GuildId, ticket.SubjectUserId.Value, cancellationToken);
                // Active refers to the original audit identity. Newer bans/removals cancel
                // its request during audit ingestion, even if a request was attempted.
                originalActionUnchanged = IsActive(ticket) && !superseded;
            }

            if (confirmed)
            {
                DateTimeOffset? expiry = ticket.PendingExpiryUtc > now ? ticket.PendingExpiryUtc : null;
                ticket.ConfirmOperation(expiry);
                await SaveModerationAsync(dbContext, ticket, cancellationToken);
                return ticket.Status == TicketStatus.Expired ? "The action has expired." : "The live timeout change was confirmed.";
            }

            var timeoutEnded = ticket.Action == TicketAction.Timeout && (liveExpiry is null || liveExpiry <= now);

            if (timeoutEnded)
            {
                ticket.Status = ticket.ExpiresAtUtc <= now ? TicketStatus.Expired : TicketStatus.Reverted;
                ticket.ClearOperation();
                ticket.Version++;
                ticket.UpdateCompletion();
                await SaveModerationAsync(dbContext, ticket, cancellationToken);
                return "The timeout ended before the correction could be confirmed. The request was cancelled.";
            }

            // Give delayed audit entries a pass to arrive. Keep the operation marker:
            // this is reconciliation on the normal cadence, not exactly-once execution.
            var awaitingAudit = ticket.PendingOperationAttempted && ticket.PendingRequestedAtUtc > now.AddSeconds(-30);

            if (originalActionUnchanged && !awaitingAudit)
                return await ApplyOperationAsync(dbContext, ticket, cancellationToken);

            return "The Discord change is still uncertain. Recovery will reconcile it on a later pass.";
        }

        if (!IsActive(ticket) || ticket.ExpiresAtUtc is null || ticket.ExpiresAtUtc > now)
            return null;

        if (ticket.Action == TicketAction.Timeout)
        {
            DateTimeOffset? liveExpiry = await discord.GetTimeoutAsync(ticket.GuildId, ticket.SubjectUserId.Value, cancellationToken);

            // Never infer expiry while a newer timeout is visible but its audit is delayed.
            if (liveExpiry > now)
                return "A live timeout is awaiting audit reconciliation.";

            ticket.Status = TicketStatus.Expired;
            ticket.Version++;
            ticket.UpdateCompletion();
            await SaveModerationAsync(dbContext, ticket, cancellationToken);
            return "The timeout has expired.";
        }

        if (ticket.Action != TicketAction.Ban || superseded)
            return null;

        if (!await discord.IsBannedAsync(ticket.GuildId, ticket.SubjectUserId.Value, cancellationToken))
        {
            ticket.Status = TicketStatus.Reverted;
            ticket.Version++;
            ticket.UpdateCompletion();
            await SaveModerationAsync(dbContext, ticket, cancellationToken);
            return "The user is already unbanned; the original ban is no longer active.";
        }

        ticket.PrepareOperation(expiry: null, editorUserId: null);
        await SaveModerationAsync(dbContext, ticket, cancellationToken);
        return await ApplyOperationAsync(dbContext, ticket, cancellationToken);
    }
}
