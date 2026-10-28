namespace SylvanBot.Application.Features.Tickets;

using Common;
using Infrastructure.Data;

public sealed partial class TicketWorkflow
{
    public async Task<TicketChange> ApproveAsync(TicketActor actor, long number, int version, CancellationToken cancellationToken)
    {
        actor = await discord.GetActorAsync(actor.GuildId, actor.UserId, cancellationToken);
        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: true, cancellationToken)
                ?? throw new BotCommandException("No matching ticket.");

            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);
            ticket.Approve(actor, version);
            await SaveModerationAsync(dbContext, ticket, cancellationToken);

            return await PublishObservedAsync(dbContext, ticket, cancellationToken);
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    public async Task<TicketChange> HideModerationAsync(TicketActor actor, long number, int version, CancellationToken cancellationToken)
    {
        actor = await discord.GetActorAsync(actor.GuildId, actor.UserId, cancellationToken);

        if (actor is { IsAdministrator: false, CanViewAuditLog: false })
            throw new BotCommandException("Hiding moderation tickets requires Administrator or View Audit Log.");

        if (!await ReconcileAuditAsync(actor.GuildId, cancellationToken))
            throw new BotCommandException("Audit history is unavailable. Try again after reconciliation.");

        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: true, cancellationToken)
                ?? throw new BotCommandException("No matching ticket.");

            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

            if (ticket.Version != version || ticket.SupersededByNumber is not null || ticket.PendingOperationId is not null)
                throw new BotCommandException("The ticket changed or has an unresolved operation. Refresh it before hiding.");

            if (ticket.Action is not (TicketAction.Ban or TicketAction.Timeout or TicketAction.VoiceMute))
                throw new BotCommandException("This ticket type cannot be reverted here.");

            ticket.HidePending = true;
            ticket.LastModifiedByUserId = actor.UserId;
            ticket.LastModifiedAtUtc = DateTimeOffset.UtcNow;
            ticket.Version++;
            await SaveModerationAsync(dbContext, ticket, cancellationToken);

            try
            {
                return await FinishHideAsync(dbContext, ticket, cancellationToken);
            }
            catch (BotCommandException)
            {
                // A rejected action is not a deferred instruction to unmute later.
                ticket.HidePending = ticket.PendingOperationId is not null;
                ticket.Version++;
                await SaveModerationAsync(dbContext, ticket, cancellationToken);

                throw;
            }
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    private async Task<TicketChange> FinishHideAsync(BotDbContext dbContext, Ticket ticket, CancellationToken cancellationToken)
    {
        if (ticket.SupersededByNumber is not null)
        {
            ticket.HidePending = false;
            ticket.Version++;
            await SaveModerationAsync(dbContext, ticket, cancellationToken);

            throw new BotCommandException("A newer restriction superseded this ticket. The hide request was cancelled.");
        }

        if (ticket.Status is not (TicketStatus.Expired or TicketStatus.Reverted) && !ticket.Hidden)
        {
            if (ticket.SubjectUserId is null || ticket.SourceAuditId is null)
                throw new BotCommandException("The original moderation action cannot be identified. The ticket remains visible.");

            if (ticket.Action == TicketAction.VoiceMute)
            {
                await discord.RevertVoiceMuteAsync(ticket, cancellationToken);
                ticket.Status = TicketStatus.Reverted;
            }
            else
            {
                if (ticket.Action == TicketAction.Timeout)
                {
                    DateTimeOffset? liveExpiry = await discord.GetTimeoutAsync(ticket.GuildId, ticket.SubjectUserId.Value, cancellationToken);

                    if (liveExpiry > DateTimeOffset.UtcNow && liveExpiry != ticket.ExpiresAtUtc)
                        throw new BotCommandException("The live timeout changed. Refresh after audit reconciliation.");
                }

                if (ticket.PendingOperationId is null)
                {
                    ticket.PrepareOperation(expiry: null, ticket.LastModifiedByUserId);
                    await SaveModerationAsync(dbContext, ticket, cancellationToken);
                    await ApplyOperationAsync(dbContext, ticket, cancellationToken);
                }
                else
                {
                    await RecoverModerationAsync(dbContext, ticket, auditCurrent: true, cancellationToken);
                }

                if (ticket.Status is not (TicketStatus.Expired or TicketStatus.Reverted))
                    throw new BotCommandException("The reversal is not confirmed. The ticket remains visible; check it after reconciliation.");
            }
        }

        if (ticket.PendingOperationId is not null)
            ticket.Status = TicketStatus.Reverted;

        ticket.Hidden = true;
        ticket.HidePending = false;
        ticket.ClearOperation();
        ticket.Version++;
        await SaveModerationAsync(dbContext, ticket, cancellationToken);

        return await RemoveCopiesAsync(ticket, cancellationToken);
    }
}