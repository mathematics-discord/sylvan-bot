using static SylvanBot.Application.Features.Tickets.TicketOperationCommand;

namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class BanTicketStrategy(DiscordSocketClient client, TicketBoundary boundary, ILogger logger) : ITicketStrategy
{
    public TicketAction Action => TicketAction.Ban;

    public async Task<TicketResult> HandleAsync(BotDbContext db, TicketRequest request, CancellationToken cancellationToken)
    {
        switch (request.Value)
        {
            case ObserveModeration observed:
                return await RecordObservedAsync(db, observed, cancellationToken);
            case EditDetails edit:
                if (edit.Details.HasDuration)
                    edit.Ticket.SetDuration(edit.Details.DurationSeconds == long.MaxValue ? null : edit.Details.DurationSeconds, edit.Actor.UserId);

                await db.SaveChangesAsync(cancellationToken);

                return new TicketResult([edit.Ticket.Id], "Ticket details saved. Due bans will expire after audit reconciliation.");
            case ReconcileTicket reconcile:
                return await ReconcileAsync(db, reconcile.Ticket, reconcile.AuditCurrent, cancellationToken);
            default:
                throw new ArgumentException("Unsupported ban request.", nameof(request));
        }
    }

    // Strategies share audit identity, attribution and supersession rules.
    internal static async Task<TicketResult> RecordObservedAsync(BotDbContext db, ObserveModeration request, CancellationToken token)
    {
        var observation = request.Observation;

        var alreadyRecorded = await db.Tickets
           .AnyAsync(
                ticket => ticket.GuildId == observation.GuildId
                    && ticket.SourceAuditId == observation.AuditId
                    && ticket.Action == observation.Action
                    && ticket.RoleId == observation.RoleId,
                token);

        if (alreadyRecorded)
            return new TicketResult([], string.Empty);

        var latest = await db.Tickets
           .Where(ticket => ticket.GuildId == observation.GuildId
                      && ticket.SubjectUserId == observation.SubjectUserId
                      && ticket.Action == observation.Action
                      && ticket.RoleId == observation.RoleId
                      && ticket.SourceAuditId != null)
           .OrderByDescending(ticket => ticket.SourceAuditId)
           .FirstOrDefaultAsync(token);

        var operation = observation.OperationId.HasValue
            ? await db.Tickets
               .SingleOrDefaultAsync(
                    ticket => ticket.GuildId == observation.GuildId
                        && ticket.SubjectUserId == observation.SubjectUserId
                        && ticket.Action == observation.Action
                        && ticket.RoleId == observation.RoleId
                        && ticket.Pending != null
                        && ticket.Pending.Id == observation.OperationId.Value,
                    token)
            : null;

        List<Ticket> affected = [];
        var older = observation.Action != TicketAction.Kick && latest is not null && latest.LastAuditId >= observation.AuditId;

        if (observation is { Action: TicketAction.Timeout, OperationId.HasValue: true } && operation is null)
        {
            if (latest is null || older)
                return new TicketResult([], string.Empty);

            latest.LastAuditId = observation.AuditId;
            await db.SaveChangesAsync(token);

            return new TicketResult([], string.Empty);
        }

        if (operation is not null && operation.LastAuditId < observation.AuditId)
        {
            if (operation.Pending!.Command.Value is ChangeTimeout timeout)
            {
                var nativeExpiry = operation.Pending.NativeExpiryUtc ?? timeout.ExpiryUtc;

                var matches = timeout.ExpiryUtc <= observation.CreatedAtUtc
                    ? observation.Removed
                    : observation.Timeout.Value is TicketTimeout.Until applied && applied.ExpiresAtUtc == nativeExpiry;

                if (!matches || operation.Status != TicketStatus.Active || operation.ExpirySuspended)
                    return new TicketResult([], string.Empty);

                operation.ConfirmTimeoutChange(observation.CreatedAtUtc);
            }

            if (observation.Removed && operation.Status != TicketStatus.Superseded)
                operation.End(operation.ExpiresAtUtc <= observation.CreatedAtUtc ? TicketStatus.Expired : TicketStatus.Reverted);

            operation.Pending = null;
            operation.LastAuditId = observation.AuditId;
            operation.Record(TicketHistoryAction.AuditObserved, observation.ModeratorUserId, observation.CreatedAtUtc);
            affected.Add(operation);
        }

        var config = await db.TicketConfigurations
           .SingleAsync(value => value.GuildId == observation.GuildId, token);

        if (observation.RestoredTicketNumber is { } restoredNumber)
        {
            var restored = await db.Tickets
               .SingleOrDefaultAsync(
                    ticket => ticket.GuildId == observation.GuildId
                        && ticket.Number == restoredNumber
                        && ticket.Action == TicketAction.Role
                        && ticket.SubjectUserId == observation.SubjectUserId
                        && ticket.RoleId == observation.RoleId,
                    token);

            if (restored is not null && restored.LastAuditId < observation.AuditId)
            {
                restored.LastAuditId = observation.AuditId;
                await db.SaveChangesAsync(token);
            }

            return new TicketResult([], string.Empty);
        }

        var tracked = observation.Action != TicketAction.Role
            || (observation.RoleId is { } role && config.TrackedRoleIds.Contains(checked((long)role)));

        Ticket? created = null;

        if (observation is { CreatesTicket: true, OperationId.HasValue: false } && tracked && config.CaptureFromUtc <= observation.CreatedAtUtc)
        {
            var number = await db.Database
               .SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"")
               .SingleAsync(token);

            var details = ReadDetails(observation);

            long? duration = details.HasDuration && details.DurationSeconds != long.MaxValue
                ? details.DurationSeconds
                : null;

            DateTimeOffset? expiry = observation.Timeout.Value is TicketTimeout.Until until
                ? until.ExpiresAtUtc
                : null;

            if (expiry is { } end)
                duration = Math.Max(1, (long)Math.Ceiling((end - observation.CreatedAtUtc).TotalSeconds));

            if (observation.Action == TicketAction.Kick)
                duration = null;

            created = new Ticket
            {
                GuildId = observation.GuildId, Number = number, Action = observation.Action, RoleId = observation.RoleId,
                SubjectUserId = observation.SubjectUserId, ModeratorUserId = observation.ModeratorUserId,
                CreatedAtUtc = observation.CreatedAtUtc, SourceAuditId = observation.AuditId, LastAuditId = observation.AuditId,
                AuditReason = observation.Reason, ReviewExempt = request.ReviewExempt, ReminderExempt = observation.ModeratorIsBot,
            };

            created.Initialise(details.Comment.Apply(string.Empty), duration, expiry);

            if (older)
                created.End(TicketStatus.Superseded, observation.ModeratorUserId, latest!.Number);

            db.Tickets.Add(created);
            affected.Add(created);
        }

        var updateLatest = observation.Action != TicketAction.Kick
            && latest is not null
            && !older
            && latest != operation
            && (!observation.OperationId.HasValue || operation is not null);

        if (updateLatest)
        {
            if (created is not null)
            {
                latest!.End(TicketStatus.Superseded, observation.ModeratorUserId, created.Number);
            }
            else if (!tracked && observation.CreatesTicket)
            {
                // An untracked new grant belongs to the moderator, not the old restriction.
                latest!.End(TicketStatus.Reverted);
            }
            else if (observation.Removed)
            {
                latest!.End(
                    latest.ExpiresAtUtc <= observation.CreatedAtUtc ? TicketStatus.Expired : TicketStatus.Reverted,
                    observation.ModeratorUserId);
            }
            else if (observation.Timeout.Value is TicketTimeout.Until timeout && latest!.Status == TicketStatus.Active)
            {
                latest.SetDuration(
                    Math.Max(1, (long)Math.Ceiling((timeout.ExpiresAtUtc - latest.CreatedAtUtc).TotalSeconds)),
                    observation.ModeratorIsBot ? null : observation.ModeratorUserId,
                    timeout.ExpiresAtUtc);

                latest.ObserveTimeoutWindow(timeout.ExpiresAtUtc, observation.CreatedAtUtc);
            }

            var confirmationFollows = latest!.Pending is { } pending
                && request.OperationAudits.TryGetValue(pending.Id, out var audit)
                && audit > observation.AuditId;

            if (!confirmationFollows)
                latest.Pending = null;

            latest.LastAuditId = observation.AuditId;
            latest.Record(TicketHistoryAction.AuditObserved, observation.ModeratorUserId, observation.CreatedAtUtc);
            affected.Add(latest);
        }

        await db.SaveChangesAsync(token);

        ImmutableArray<long> ids = affected
           .Select(ticket => ticket.Id)
           .ToImmutableArray();

        return new TicketResult(ids, string.Empty);
    }

    private static TicketDetails ReadDetails(TicketObservation observation)
    {
        var reason = observation.Reason;

        if (observation.ModeratorIsBot)
            return TicketDetails.ParseFields(null, string.IsNullOrWhiteSpace(reason) ? "No reason attached to the audit log." : reason);

        try
        {
            return string.IsNullOrWhiteSpace(reason)
                ? TicketDetails.ParseFields(null, null)
                : TicketDetails.ParseReply(reason);
        }
        catch (BotCommandException)
        {
            return TicketDetails.ParseFields(null, reason);
        }
    }

    private async Task<TicketResult> ReconcileAsync(BotDbContext db, Ticket ticket, bool auditCurrent, CancellationToken token)
    {
        var version = ticket.Version;
        var now = DateTimeOffset.UtcNow;

        if (ticket.SourceAuditId is null)
            ticket.ExpirySuspended = true;

        if (ticket.Status != TicketStatus.Active)
            ticket.Pending = null;

        if (!auditCurrent
            || ticket.ExpirySuspended
            || ticket.Status != TicketStatus.Active
            || ticket.ExpiresAtUtc is null
            || ticket.ExpiresAtUtc > now)
            return new TicketResult([], string.Empty);

        // Recheck immediately before unban. A new audit entry must be ingested first.
        var configuration = await db.TicketConfigurations.SingleAsync(value => value.GuildId == ticket.GuildId, token);

        var scan = await boundary.ReadAuditAsync(ticket.GuildId, configuration.LastAuditScanAtUtc!.Value.AddMinutes(-10), token);

        var changed = scan.Observations
           .Any(value => value.Action == Action
                    && value.SubjectUserId == ticket.SubjectUserId
                    && value.AuditId > ticket.LastAuditId
                    && (!value.OperationId.HasValue || ticket.Pending is null || value.OperationId.Value != ticket.Pending.Id));

        if (changed)
            return new TicketResult([], "A newer ban change is awaiting audit reconciliation.");

        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(token));
        var ban = await guild.GetBanAsync(ticket.SubjectUserId, TicketBoundary.Options(token));

        if (ban is null)
        {
            ticket.End(ticket.Pending is null ? TicketStatus.Reverted : TicketStatus.Expired);
            ticket.Pending = null;
        }
        else
        {
            if (ticket.Pending is { Attempted: true } pending && pending.RequestedAtUtc > now.AddSeconds(-30))
                return new TicketResult([], "The scheduled unban is awaiting confirmation.");

            ticket.Pending ??= new TicketOperation { Kind = "RemoveBan" };
            await db.SaveChangesAsync(token);
            ticket.Pending.Attempted = true;
            await db.SaveChangesAsync(token);
            var options = TicketBoundary.Options(token);
            options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {ticket.Pending.Id:D}";

            try
            {
                await guild.RemoveBanAsync(ticket.SubjectUserId, options);
                ticket.End(TicketStatus.Expired);
                ticket.Pending = null;
            }
            catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden)
            {
                // A definitive rejection leaves the restriction intact and the expiry retryable.
                ticket.Pending = null;
                await db.SaveChangesAsync(token);

                return new TicketResult([ticket.Id], "Discord rejected the scheduled unban. Check Sylvan's Ban Members permission.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.Warning(
                    "Ticket {TicketId} in guild {GuildId}: moderation outcome is uncertain ({FailureType})",
                    ticket.Id,
                    ticket.GuildId,
                    exception.GetType().Name);

                return new TicketResult([ticket.Id], "The unban could not be confirmed. Recovery will reconcile it; do not repeat it blindly.");
            }
        }

        await db.SaveChangesAsync(token);

        return new TicketResult(ticket.Version == version ? [] : [ticket.Id], "Ban expiry reconciled.");
    }
}