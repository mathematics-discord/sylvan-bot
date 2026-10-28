using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Common;

public sealed partial class TicketWorkflow
{
    public async Task<bool> ReconcileAuditAsync(ulong guildId, CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var configuration = await dbContext.TicketConfigurations
               .AsNoTracking()
               .SingleAsync(configuration => configuration.GuildId == guildId, cancellationToken);

            if (!configuration.Enabled || configuration.DestinationChannelId is null || configuration.CaptureFromUtc is null)
                return false;

            var scanStartedUtc = DateTimeOffset.UtcNow;

            if ((configuration.LastAuditScanAtUtc ?? configuration.CaptureFromUtc) < scanStartedUtc.AddDays(-45))
            {
                await SuspendBanExpiryAsync(guildId, cancellationToken);

                logger.LogWarning(
                    "Audit recovery in guild {GuildId} exceeds Discord retention; existing ban expires require explicit correction",
                    guildId);
            }

            var boundary = configuration.LastAuditScanAtUtc?.AddMinutes(-10) ?? configuration.CaptureFromUtc.Value;

            (ulong? LastAuditId, ImmutableArray<TicketObservation> Observations) scan =
                await discord.ReadAuditAsync(guildId, boundary, cancellationToken);

            var identitiesResolved = await RecordScanAsync(guildId, configuration, scan, cancellationToken);

            // Do not advance the coverage boundary past unresolved action identities.
            return identitiesResolved && await CompleteAuditScanAsync(guildId, scanStartedUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Audit recovery failed in guild {GuildId} ({FailureType}); live expiry is suspended for this pass",
                guildId,
                exception.GetType().Name);

            return false;
        }
    }

    private static bool IsActive(Ticket ticket)
        => ticket.Status is TicketStatus.Active or TicketStatus.ExpirationFailed;

    private static TicketDetails ReadAuditDetails(TicketObservation observation)
    {
        TicketDetails details;

        try
        {
            details = string.IsNullOrWhiteSpace(observation.Reason)
                ? new TicketDetails(hasDuration: false, durationSeconds: null, comment: null)
                : TicketDetails.ParseReply(observation.Reason);
        }
        catch (BotCommandException)
        {
            // An audit reason is evidence even when it is not valid completion syntax.
            details = new TicketDetails(hasDuration: false, durationSeconds: null, observation.Reason);
        }

        if (observation.ModeratorUserId is null)
            details = new TicketDetails(hasDuration: false, durationSeconds: null, observation.Reason);

        if (observation.ModeratorIsBot)
        {
            // Other bots' text cannot assign a human or schedule an unban.
            details = new TicketDetails(
                hasDuration: false,
                durationSeconds: null,
                string.IsNullOrWhiteSpace(observation.Reason)
                    ? "No reason attached to the audit log."
                    : observation.Reason);

            // The Discord mapper admits this policy only for our own bot identity.
            if (observation.PermanentBotAction)
                details = new TicketDetails(hasDuration: true, durationSeconds: null, observation.Reason);
        }

        return details;
    }

    private static Ticket CreateObservedTicket(TicketObservation observation, long number)
    {
        var details = ReadAuditDetails(observation);

        var ticket = new Ticket
        {
            GuildId = observation.GuildId,
            Number = number,
            Action = observation.Action,
            SubjectUserId = observation.SubjectUserId,
            ModeratorUserId = observation.ModeratorUserId,
            CreatedAtUtc = observation.CreatedAtUtc,
            SourceAuditId = observation.AuditId,
            LastAuditId = observation.AuditId,
            AuditReason = observation.Reason,
            Comment = details.Comment,
            DurationKnown = details.HasDuration,
            DurationSeconds = details.DurationSeconds,
            Completion = observation.ModeratorIsBot ? TicketCompletion.Automated : TicketCompletion.Pending,
            Status = TicketStatus.Active,
        };

        if (observation is { Action: TicketAction.Timeout, ExpiryUtc: not null })
        {
            ticket.SetObservedExpiry(observation.ExpiryUtc.Value);

            if (observation.ExpiryUtc <= DateTimeOffset.UtcNow)
                ticket.Status = TicketStatus.Expired;
        }
        else if (details.HasDuration)
        {
            try
            {
                ticket.ExpiresAtUtc = details.Expiry(observation.CreatedAtUtc);
            }
            catch (BotCommandException)
            {
                ticket.DurationKnown = false;
                ticket.DurationSeconds = null;
            }
        }

        ticket.UpdateCompletion();

        return ticket;
    }

    private static void UpdateObservedAction(Ticket latest, Ticket? created, TicketObservation observation, IReadOnlyDictionary<Guid, ulong> operationAudits)
    {
        var hasCurrentAction = IsActive(latest) || latest.PendingOperationId is not null;

        if (observation.CreatesTicket && hasCurrentAction)
        {
            latest.Status = TicketStatus.Reverted;
            latest.SupersededByNumber = created?.Number;
        }
        else if (observation.Removed && hasCurrentAction)
        {
            latest.Status = observation.Action == TicketAction.Timeout
                && latest.ExpiresAtUtc <= observation.CreatedAtUtc
                    ? TicketStatus.Expired
                    : TicketStatus.Reverted;
        }
        else if (observation.Action == TicketAction.Timeout && IsActive(latest) && observation.ExpiryUtc is not null)
        {
            latest.SetObservedExpiry(observation.ExpiryUtc.Value);
        }
        else
        {
            return;
        }

        // Keep the marker when this scan also proves our attempted write followed
        // the intervening action. Its later audit will confirm the actual result.
        var confirmationFollows = latest.PendingOperationId is not null
            && operationAudits.TryGetValue(
                latest.PendingOperationId.Value,
                out var confirmationAuditId)
            && confirmationAuditId > observation.AuditId;

        if (!confirmationFollows)
            latest.CancelOlderOperation(observation);

        latest.LastAuditId = observation.AuditId;
        latest.LastModifiedByUserId = observation.ModeratorUserId;
        latest.LastModifiedAtUtc = observation.CreatedAtUtc;
        latest.Version++;
        latest.UpdateCompletion();
    }

    private static void ConfirmObservedOperation(Ticket operationTicket, Ticket? latest, TicketObservation observation, bool olderThanKnownChange)
    {
        operationTicket.LastAuditId = observation.AuditId;
        operationTicket.ConfirmOperation(observation.Removed ? null : observation.ExpiryUtc);

        // A different moderator can act between our final read and Discord's write.
        // Retain the requested ticket's correction and reflect the actual resulting
        // timeout on the newer record without changing either original attribution.
        if (latest is null || latest.Id == operationTicket.Id || olderThanKnownChange || !IsActive(latest))
            return;

        if (observation.Removed)
            latest.Status = TicketStatus.Reverted;
        else if (observation.ExpiryUtc is not null)
            latest.SetObservedExpiry(observation.ExpiryUtc.Value);

        latest.LastAuditId = observation.AuditId;
        latest.LastModifiedByUserId = operationTicket.LastModifiedByUserId;
        latest.LastModifiedAtUtc = observation.CreatedAtUtc;
        latest.Version++;
        latest.UpdateCompletion();
    }

    private async Task<bool> CompleteAuditScanAsync(
        ulong guildId,
        DateTimeOffset scannedFromUtc,
        CancellationToken cancellationToken)
    {
        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var configuration = await dbContext.TicketConfigurations
               .SingleAsync(configuration => configuration.GuildId == guildId, cancellationToken);

            if (!configuration.Enabled || configuration.CaptureFromUtc > scannedFromUtc)
                return false;

            if (configuration.LastAuditScanAtUtc is null || configuration.LastAuditScanAtUtc < scannedFromUtc)
            {
                configuration.LastAuditScanAtUtc = scannedFromUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return true;
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    private async Task RecordAuditAsync(
        ulong guildId,
        ulong auditId,
        TicketObservation? observation,
        IReadOnlyDictionary<Guid, ulong> operationAudits,
        CancellationToken cancellationToken)
    {
        if (observation is not null && (observation.GuildId != guildId || observation.AuditId != auditId))
            throw new ArgumentException("The audit entry belongs to another guild or scan.", nameof(observation));

        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var configuration = await dbContext.TicketConfigurations
               .SingleAsync(configuration => configuration.GuildId == guildId, cancellationToken);

            if (!configuration.Enabled || configuration.DestinationChannelId is null)
                return;

            if (observation is not null)
                await ApplyObservationAsync(dbContext, configuration, observation, operationAudits, cancellationToken);

            if (configuration.LastAuditId is null || auditId > configuration.LastAuditId)
                configuration.LastAuditId = auditId;

            // The record and cursor succeed together. Delivery is a separate, recoverable step.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    private async Task ApplyObservationAsync(
        Infrastructure.Data.BotDbContext dbContext,
        TicketConfiguration configuration,
        TicketObservation observation,
        IReadOnlyDictionary<Guid, ulong> operationAudits,
        CancellationToken cancellationToken)
    {
        var alreadyRecorded = await dbContext.Tickets
           .AnyAsync(
                ticket => ticket.GuildId == observation.GuildId && ticket.SourceAuditId == observation.AuditId,
                cancellationToken);

        if (alreadyRecorded)
            return;

        Ticket? latest = null;

        if (observation.SubjectUserId is not null)
        {
            latest = await dbContext.Tickets
               .Where(ticket => ticket.GuildId == observation.GuildId
                          && ticket.SubjectUserId == observation.SubjectUserId
                          && ticket.Action == observation.Action
                          && ticket.SourceAuditId != null)
               .OrderByDescending(ticket => ticket.SourceAuditId)
               .FirstOrDefaultAsync(cancellationToken);
        }

        var olderThanKnownChange = latest?.LastAuditId >= observation.AuditId;

        var operationTicket = observation.OperationId is null
            ? null
            : await dbContext.Tickets
               .SingleOrDefaultAsync(
                    ticket => ticket.GuildId == observation.GuildId
                        && ticket.SubjectUserId == observation.SubjectUserId
                        && ticket.Action == observation.Action
                        && ticket.PendingOperationId == observation.OperationId,
                    cancellationToken);

        if (operationTicket is not null && operationTicket.LastAuditId < observation.AuditId)
        {
            ConfirmObservedOperation(operationTicket, latest, observation, olderThanKnownChange);

            return;
        }

        var created = await CaptureObservationAsync(dbContext, configuration, observation, latest, olderThanKnownChange, cancellationToken);

        if (latest is null || olderThanKnownChange)
            return;

        // A synchronization marker has authority only while its exact persisted request exists.
        if (observation.OperationId is not null)
            return;

        UpdateObservedAction(latest, created, observation, operationAudits);
    }

    private async Task<bool> RecordScanAsync(
        ulong guildId,
        TicketConfiguration configuration,
        (ulong? LastAuditId, ImmutableArray<TicketObservation> Observations) scan,
        CancellationToken cancellationToken)
    {
        var identitiesResolved = true;
        Dictionary<Guid, ulong> operationAudits = new Dictionary<Guid, ulong>();

        foreach (var observation in scan.Observations)
        {
            if (observation.OperationId is not null)
                operationAudits[observation.OperationId.Value] = observation.AuditId;
        }

        foreach (var observation in scan.Observations)
        {
            if (observation.SubjectUserId is null || observation.ModeratorUserId is null)
                identitiesResolved = false;

            var missingIdentity = (observation.SubjectUserId is null || observation.ModeratorUserId is null)
                && (configuration.LastAuditId is null
                    || observation.AuditId > configuration.LastAuditId);

            if (missingIdentity)
                logger.LogWarning(
                    "Audit entry {AuditId} in guild {GuildId} has unresolved user data; retaining available facts without inferred attribution",
                    observation.AuditId,
                    guildId);

            await RecordAuditAsync(guildId, observation.AuditId, observation, operationAudits, cancellationToken);
        }

        if (scan.LastAuditId is not null)
            await RecordAuditAsync(
                guildId,
                scan.LastAuditId.Value,
                observation: null,
                operationAudits,
                cancellationToken);

        return identitiesResolved;
    }

    private async Task<Ticket?> CaptureObservationAsync(
        Infrastructure.Data.BotDbContext dbContext,
        TicketConfiguration configuration,
        TicketObservation observation,
        Ticket? latest,
        bool olderThanKnownChange,
        CancellationToken cancellationToken)
    {
        var createsTicket = observation.CreatesTicket;

        var captureNew = createsTicket
            && observation.OperationId is null
            && configuration.Enabled
            && configuration.CaptureFromUtc <= observation.CreatedAtUtc;

        Ticket? created = null;

        if (captureNew)
        {
            var number = await dbContext.Database
               .SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"")
               .SingleAsync(cancellationToken);

            created = CreateObservedTicket(observation, number);

            if (created.ModeratorUserId is not null)
                created.ReviewExempt = await discord.IsReviewExemptAsync(
                    guildId: created.GuildId,
                    moderatorId: created.ModeratorUserId.Value,
                    roleIds: configuration.ReviewExemptRoleIds,
                    cancellationToken);

            if (olderThanKnownChange)
            {
                created.Status = TicketStatus.Reverted;
                created.SupersededByNumber = latest!.Number;
            }

            dbContext.Tickets.Add(created);
        }

        return created;
    }
}
