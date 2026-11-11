namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RolePersistence;

public sealed partial class TicketCoordinator(
    IDbContextFactory<BotDbContext> contexts,
    DiscordSocketClient client,
    TicketExpiry expiry,
    TicketTimeoutEnforcement timeouts,
    RolePersistenceWorker persistence,
    TicketBoundary boundary,
    TicketPublisher publisher,
    TicketReminders reminders,
    TicketReviewSummary reviewSummary,
    TicketLocks locks,
    SylvanBot.Features.Automod.AutomodRuntime automod,
    ILogger logger)
{
    public async Task<string> CreateAsync(TicketActor actor, ulong subject, string comment, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        await PrepareNoteAsync(db, actor, subject, token);
        Ticket.ValidateComment(comment);

        var number = await db.Database
            .SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"")
            .SingleAsync(token);

        var ticket = new Ticket
        {
            GuildId = actor.GuildId,
            Number = number,
            SubjectUserId = subject,
            ModeratorUserId = actor.UserId,
            Action = TicketAction.Note,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        ticket.Initialise(comment, null);
        db.Tickets.Add(ticket);

        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BotCommandException(
                $"Saving ticket #{number} could not be confirmed. Read the record before creating another note.",
                exception);
        }

        return Queued($"Saved ticket #{number}.", changed: true);
    }

    public async Task PrepareNoteAsync(TicketActor actor, ulong subject, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        await PrepareNoteAsync(db, actor, subject, token);
    }

    public async Task<string> EditAsync(
        TicketActor actor,
        long number,
        int? version,
        TicketDetails details,
        bool? hidden,
        CancellationToken ct)
    {
        using var timing = new OperationTiming(logger.ForContext("TicketNumber", number), "ticket.edit", "gate");
        var gate = await LockTicketAsync(actor.GuildId, number, ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);

            var ticket = await FindAsync(db, actor, number, ct);
            timing.Next("edit");
            var originalVersion = ticket.Version;
            var outcome = string.Empty;
            var hasDetails = details.HasDuration || details.Comment.Value is not TicketCommentEdit.Keep;

            if (hidden is not null && hasDetails && ticket.Action != TicketAction.Note)
                throw new BotCommandException("Change visibility separately from ticket details.");

            if (hasDetails)
            {
                ticket.RequireEdit(actor, version ?? ticket.Version);
                ticket.ValidateEdit(details);
            }

            if (hidden is { } visibility)
            {
                ticket.RequireVisibilityChange(actor, version ?? ticket.Version);

                if (visibility && ticket.Status == TicketStatus.Active)
                    await RevertAsync(db, actor, ticket, ticket.Version, ct);

                ticket.SetHidden(actor, ticket.Version, visibility);
            }

            if (hidden is null || hasDetails)
            {
                if (hidden is null)
                    ticket.RequireEdit(actor, version ?? ticket.Version);

                var comment = ticket.ValidateEdit(details);

                // Editors submit their pre-filled duration even when only the comment changed.
                if (details.ChangesDuration(ticket.DurationSeconds))
                {
                    if (ticket.Action == TicketAction.Timeout)
                    {
                        outcome = await timeouts.EditAsync(db, ticket, actor, details, ct);
                    }
                    else
                    {
                        ticket.SetDuration(
                            details.DurationSeconds == long.MaxValue ? null : details.DurationSeconds,
                            actor.UserId,
                            canReview: actor.IsAdministrator || actor.CanViewAuditLog);
                    }
                }

                if (details.Comment.Value is not TicketCommentEdit.Keep)
                    ticket.SetComment(comment, actor.UserId, details.Comment.Value is TicketCommentEdit.Append);
            }

            timing.Next("save");
            await SaveAsync(db, ct);

            var changed = ticket.Version != originalVersion;

            var text = !changed ? $"Ticket #{number}: nothing changed." : $"Saved ticket #{number}.";

            return Queued(text + (outcome.Length > 0 ? "\n" + outcome : string.Empty), changed);
        }
        finally
        {
            gate.Dispose();
        }
    }

    public async Task<string> RevertAsync(TicketActor actor, long number, int? version, CancellationToken ct)
    {
        var gate = await LockTicketAsync(actor.GuildId, number, ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var ticket = await FindAsync(db, actor, number, ct);
            await RevertAsync(db, actor, ticket, version ?? ticket.Version, ct);

            return Queued($"Reverted ticket #{number}. The record is retained.", changed: true);
        }
        finally
        {
            gate.Dispose();
        }
    }

    private async Task RevertAsync(BotDbContext db, TicketActor actor, Ticket ticket, int version, CancellationToken ct)
    {
        ticket.RequireEdit(actor, version);

        switch (ticket.Action)
        {
            case TicketAction.VoiceMute or TicketAction.VoiceDeafen:
                throw new BotCommandException("Lift this voice restriction in Discord, then wait for audit reconciliation before hiding its ticket.");
            case TicketAction.Note or TicketAction.Kick:
                throw new BotCommandException("Notes and kicks have no active restriction to revert.");
        }

        if (ticket.Status != TicketStatus.Active)
            throw new BotCommandException("This ticket no longer has an active restriction.");

        var permitted = actor.IsAdministrator
            || ticket.Action switch
            {
                TicketAction.Ban => actor.CanBanMembers,
                TicketAction.Timeout => actor.CanTimeoutMembers,
                TicketAction.Role => actor.CanManageRoles,
                _ => false,
            };

        if (!permitted)
            throw new BotCommandException("You need the Discord permission for this restriction to revert it.");

        var config = await EnabledAsync(db, actor.GuildId, ct);
        DateTimeOffset? from = config.LastAuditScanAtUtc;

        if (ticket.SourceAuditId is null || from is null || from < DateTimeOffset.UtcNow.AddDays(-45))
            throw new BotCommandException("Refresh after audit reconciliation before reverting this restriction.");

        var scan = await boundary.ReadAuditAsync(actor.GuildId, from.Value.AddMinutes(-10), ct);

        var newer = scan.Observations.Any(value => value.Action == ticket.Action
                                              && value.SubjectUserId == ticket.SubjectUserId
                                              && value.RoleId == ticket.RoleId
                                              && value.AuditId > ticket.LastAuditId);

        if (newer)
            throw new BotCommandException("A newer change awaits audit reconciliation. Refresh before reverting.");

        var roleGate = ticket.Action == TicketAction.Role ? persistence.Gate(actor.GuildId) : null;

        if (roleGate is not null)
            await roleGate.WaitAsync(ct);

        try
        {
            await RevertRestrictionAsync(actor, ticket, ct);
            ticket.CompleteStaffReversal(actor.UserId, DateTimeOffset.UtcNow);

            if (ticket.Action == TicketAction.Role)
            {
                await db.PendingRoles
                    .Where(value => value.GuildId == actor.GuildId
                               && value.UserId == ticket.SubjectUserId
                               && value.RoleId == ticket.RoleId)
                    .ExecuteDeleteAsync(ct);
            }

            await SaveAsync(db, ct);
        }
        finally
        {
            roleGate?.Release();
        }
    }

    private async Task RevertRestrictionAsync(TicketActor actor, Ticket ticket, CancellationToken ct)
    {
        var options = TicketBoundary.Options(ct);
        options.AuditLogReason = $"Revert Sylvan ticket {ticket.Number}; moderator {actor.UserId}";
        var guild = await client.Rest.GetGuildAsync(actor.GuildId, options);
        var moderator = await boundary.GetMemberAsync(actor.GuildId, actor.UserId, ct)
            ?? throw new BotCommandException("The responsible moderator is no longer available.");
        var member = await guild.GetUserAsync(ticket.SubjectUserId, options);
        var bot = (IGuildUser?)client.GetGuild(actor.GuildId)?.CurrentUser
            ?? await guild.GetCurrentUserAsync(options);

        var moderatorPosition = moderator.RoleIds
            .Select(id => guild.GetRole(id)?.Position ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        var botPosition = bot.RoleIds
            .Select(id => guild.GetRole(id)?.Position ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        if (ticket.SubjectUserId == actor.UserId || ticket.SubjectUserId == guild.OwnerId)
            throw new BotCommandException("You cannot revert a restriction on yourself or the server owner.");

        if (member is not null)
        {
            var targetPosition = member.RoleIds
                .Select(id => guild.GetRole(id)?.Position ?? 0)
                .DefaultIfEmpty(0)
                .Max();

            var hierarchyBlocked = (actor.UserId != guild.OwnerId && moderatorPosition <= targetPosition)
                || botPosition <= targetPosition;

            if (hierarchyBlocked)
                throw new BotCommandException("The member must be below both you and Sylvan in the role hierarchy.");
        }

        var timeoutChanged = ticket.Action == TicketAction.Timeout
            && member is not null
            && member.TimedOutUntil != ticket.AppliedTimeoutUntilUtc;

        if (timeoutChanged)
            throw new BotCommandException("The live timeout changed. Refresh after audit reconciliation.");

        if (ticket.Action == TicketAction.Role)
        {
            var role = guild.GetRole(ticket.RoleId!.Value);

            var cannotManageRole = role is null
                || role.IsManaged
                || role.Id == guild.Id
                || role.Position >= botPosition
                || (actor.UserId != guild.OwnerId && role.Position >= moderatorPosition);

            if (cannotManageRole)
                throw new BotCommandException("You cannot manage this ticket's role.");
        }

        try
        {
            switch (ticket.Action)
            {
                case TicketAction.Ban:
                    await guild.RemoveBanAsync(ticket.SubjectUserId, options);
                    break;
                case TicketAction.Timeout:
                    if (member is not null)
                        await member.RemoveTimeOutAsync(options);

                    break;
                case TicketAction.Role:
                    if (member is not null)
                        await member.RemoveRoleAsync(ticket.RoleId!.Value, options);

                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning("Revert could not be confirmed for ticket {TicketId}: {Failure}", ticket.Id, exception.GetType().Name);

            throw new BotCommandException(
                "Discord could not confirm the reversal. Check permissions, hierarchy and member state, then refresh after audit reconciliation before retrying.",
                exception);
        }
    }

    public async Task<string> ApproveAsync(TicketActor actor, long number, int version, CancellationToken ct)
    {
        RequireStaff(actor, hidden: true);
        using var timing = new OperationTiming(logger.ForContext("TicketNumber", number), "ticket.approve", "gate");
        var gate = await LockTicketAsync(actor.GuildId, number, ct);

        try
        {
            timing.Next("database");
            await using var db = await contexts.CreateDbContextAsync(ct);
            await EnabledAsync(db, actor.GuildId, ct);

            var ticket = await db.Tickets
                    .SingleOrDefaultAsync(value => value.GuildId == actor.GuildId && value.Number == number, ct)
                ?? throw new BotCommandException("No matching ticket in this server.");

            var previous = ticket.Version;
            ticket.Approve(actor, version);

            timing.Next("save");
            return await PersistApprovalAsync(db, ticket, previous, ct);
        }
        finally
        {
            gate.Dispose();
        }
    }

    public async Task<Ticket?> ShowAsync(TicketActor actor, long number, bool hidden, CancellationToken ct)
    {
        RequireStaff(actor, hidden);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await EnabledAsync(db, actor.GuildId, ct);

        return await db.Tickets
            .AsNoTracking()
            .SingleOrDefaultAsync(ticket => ticket.GuildId == actor.GuildId && ticket.Number == number && ticket.Hidden == hidden, ct);
    }

    public async Task<Ticket> PrepareEditAsync(TicketActor actor, long number, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var ticket = await FindAsync(db, actor, number, ct);
        ticket.RequireEdit(actor, ticket.Version);

        return ticket;
    }

    internal static IQueryable<Ticket> AwaitingApproval(IQueryable<Ticket> tickets, ulong guildId) =>
        tickets
            .Where(ticket => ticket.GuildId == guildId
                       && !ticket.Hidden
                       && ticket.ApprovedAtUtc == null
                       && ticket.Action != TicketAction.Note);

    public async Task<(Ticket? Ticket, int Ready, int Waiting)> ReviewQueueAsync(TicketActor actor, long after, CancellationToken ct)
    {
        RequireStaff(actor, hidden: true);
        using var timing = new OperationTiming(logger.ForContext("GuildId", actor.GuildId), "ticket.review_queue", "configuration");
        await using var db = await contexts.CreateDbContextAsync(ct);
        await EnabledAsync(db, actor.GuildId, ct);

        IQueryable<Ticket> pending = AwaitingApproval(db.Tickets, actor.GuildId);

        // Match Complete's Unicode whitespace semantics; the provider's
        // IsNullOrWhiteSpace translation recognises only a subset of these characters.
        const string whitespace = "\u0009\u000a\u000b\u000c\u000d\u0020\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
        var eligibility = pending
            .Select(ticket => new
            {
                Ticket = ticket,
                Ready = ticket.Comment != null && ticket.Comment.Trim(whitespace.ToCharArray()) != string.Empty
                    && (ticket.Action != TicketAction.Timeout
                        || (ticket.DurationSeconds > 0 && ticket.ExpiresAtUtc != null))
                    && ticket.Pending == null,
            });

        timing.Next("counting");
        var counts = await eligibility
            .GroupBy(ticket => ticket.Ready)
            .Select(group => new { Ready = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var ready = counts
            .Where(group => group.Ready)
            .Sum(group => group.Count);
        var waiting = counts
            .Where(group => !group.Ready)
            .Sum(group => group.Count);

        timing.Next("next_ticket");
        var next = await eligibility
            .Where(ticket => ticket.Ready && ticket.Ticket.Number > after)
            .Select(ticket => ticket.Ticket)
            .AsNoTracking()
            .OrderBy(ticket => ticket.Number)
            .FirstOrDefaultAsync(ct);

        logger.Debug("Ticket review queue in guild {GuildId}: {ReadyCount} ready, {WaitingCount} waiting", actor.GuildId, ready, waiting);

        return (next, ready, waiting);
    }

    public async Task<TicketConfiguration> ConfigureAsync(
        TicketActor actor,
        bool? enabled,
        ulong? channelId,
        int? reminderIntervalSeconds,
        CancellationToken ct,
        ulong? trackedRoleAdd = null,
        ulong? trackedRoleRemove = null)
    {
        if (actor.GuildId == 0 || actor.UserId == 0 || !actor.IsAdministrator)
            throw new BotCommandException("Administrator permission is required to configure tickets.");

        var gate = await locks.ConfigurationAsync(actor.GuildId, ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);

            var config = await db.TicketConfigurations
                .SingleOrDefaultAsync(value => value.GuildId == actor.GuildId, ct);

            if (config is null)
            {
                config = new TicketConfiguration { GuildId = actor.GuildId };
                db.TicketConfigurations.Add(config);
            }

            var destination = channelId ?? config.DestinationChannelId;

            if (reminderIntervalSeconds < 60 || ((enabled ?? config.Enabled) && destination is null))
                throw new BotCommandException("Choose a tickets channel and a reminder interval of at least one minute.");

            if (enabled == true)
                await boundary.CheckCaptureAsync(actor.GuildId, ct);

            if (enabled == true && (!config.Enabled || config.CaptureFromUtc is null))
                config.CaptureFromUtc = DateTimeOffset.UtcNow;

            var resetReminders = enabled is { } active && active != config.Enabled;

            config.Enabled = enabled ?? config.Enabled;
            config.DestinationChannelId = destination;
            config.ReminderIntervalSeconds = reminderIntervalSeconds ?? config.ReminderIntervalSeconds;

            if (trackedRoleAdd is { } added && !config.TrackedRoleIds.Contains(checked((long)added)))
                config.TrackedRoleIds.Add(checked((long)added));

            if (trackedRoleRemove is { } removed)
                config.TrackedRoleIds.Remove(checked((long)removed));

            if (enabled is not null)
                automod.CloseGuild(actor.GuildId);

            await SaveAsync(db, ct);
            reviewSummary.MarkDirty(actor.GuildId);

            if (!await automod.RefreshAsync(actor.GuildId, ct))
                throw new BotCommandException(
                    "Settings saved, but the shared ticket/Automod gate remains closed. Retry /config tickets enabled with the intended state.");

            automod.OpenGuild(actor.GuildId);

            if (!resetReminders)
                return config;

            IQueryable<Ticket> dbTickets = db.Tickets;

            List<ulong> moderators = await db.TicketModerators
                .Where(moderator => dbTickets.Any(ticket => ticket.GuildId == actor.GuildId && ticket.Id == moderator.ActiveTicketId))
                .Select(moderator => moderator.UserId)
                .ToListAsync(ct);

            foreach (var userId in moderators)
            {
                using var reminderGate = await locks.ModeratorAsync(userId, ct);

                await db.TicketModerators
                    .Where(moderator => moderator.UserId == userId
                               && dbTickets.Any(ticket => ticket.GuildId == actor.GuildId && ticket.Id == moderator.ActiveTicketId))
                    .ExecuteUpdateAsync(
                        update => update
                            .SetProperty(moderator => moderator.NextDeliveryUtc, (DateTimeOffset?)null)
                            .SetProperty(moderator => moderator.Version, moderator => moderator.Version + 1),
                        ct);
            }

            return config;
        }
        finally
        {
            gate.Dispose();
        }
    }

    public async Task RedeliverAsync(TicketActor actor, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);

        await EnabledAsync(db, actor.GuildId, token);
        RequireStaff(actor, hidden: false);

        await reminders.RemindAsync(actor.UserId, force: true, token, actor.GuildId, actor);
    }

    internal static async Task<TicketConfiguration> EnabledAsync(BotDbContext db, ulong guildId, CancellationToken token)
    {
        var config = await db.TicketConfigurations.SingleOrDefaultAsync(value => value.GuildId == guildId, token);

        return config is { Enabled: true, DestinationChannelId: not null }
            ? config
            : throw new BotCommandException("Tickets are not enabled in this server.");
    }

    internal async Task AcceptScanAsync(TicketConfiguration scannedConfig, TicketAuditScan scan, DateTimeOffset started, CancellationToken token)
    {
        using var configurationGate = await locks.ConfigurationAsync(scannedConfig.GuildId, token);
        await using var db = await contexts.CreateDbContextAsync(token);
        var config = await db.TicketConfigurations.SingleAsync(value => value.GuildId == scannedConfig.GuildId, token);

        if (config.CaptureFromUtc != scannedConfig.CaptureFromUtc)
            return;

        Dictionary<ulong, bool> reviewPermissions = [];

        async Task<bool> ResolveReviewPermissionAsync(ulong userId)
        {
            if (reviewPermissions.TryGetValue(userId, out var cached))
                return cached;

            var allowed = await boundary.CanReviewAsync(config.GuildId, userId, token);
            reviewPermissions.Add(userId, allowed);
            return allowed;
        }

        Dictionary<Guid, ulong> correlations = [];

        foreach (var value in scan.Observations)
            if (value.OperationId.TryGet(out var correlation))
                correlations[correlation] = Math.Max(correlations.GetValueOrDefault(correlation), value.AuditId);

        IEnumerable<IGrouping<ulong, TicketObservation>> groups = scan.Observations
            .GroupBy(observation => observation.AuditId);

        foreach (IGrouping<ulong, TicketObservation> group in groups)
        {
            List<long> existing = [];

            foreach (var observation in group)
                existing.AddRange(await ObservedTicketIdsAsync(db, observation, token));

            using (await locks.TicketsAsync(existing, token))
            await using (var transaction = await db.Database.BeginTransactionAsync(token))
            {
                foreach (var observation in group)
                {
                    await RecordAuditAsync(db, observation, correlations, automod.IsEnabled(config.GuildId), ResolveReviewPermissionAsync, token);
                }

                config.LastAuditId = Math.Max(config.LastAuditId ?? 0, group.Key);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }

            db.ChangeTracker.Clear();
            db.Attach(config);
        }

        config.LastAuditId = Math.Max(config.LastAuditId ?? 0, scan.LastAuditId.TryGet(out var last) ? last : 0);
        config.LastAuditScanAtUtc = started;
        await db.SaveChangesAsync(token);
    }

    private static async Task<List<long>> ObservedTicketIdsAsync(BotDbContext db, TicketObservation observation, CancellationToken token)
    {
        IQueryable<Ticket> matching = db.Tickets
            .Where(ticket => ticket.GuildId == observation.GuildId
                       && ticket.SubjectUserId == observation.SubjectUserId
                       && ticket.Action == observation.Action
                       && ticket.RoleId == observation.RoleId);

        IQueryable<long> latest = matching
            .Where(ticket => ticket.SourceAuditId != null)
            .OrderByDescending(ticket => ticket.SourceAuditId)
            .Select(ticket => ticket.Id)
            .Take(1);

        Guid? operationId = observation.OperationId.TryGet(out var operation) ? operation : (Guid?)null;

        return await matching
            .Where(ticket => latest.Contains(ticket.Id)
                       || (operationId != null && ticket.Pending != null && ticket.Pending.Id == operationId)
                       || (observation.RestoredTicketNumber != null && ticket.Number == observation.RestoredTicketNumber))
            .Select(ticket => ticket.Id)
            .ToListAsync(token);
    }

    internal async Task RecoverAsync(ulong guildId, long id, bool auditCurrent, CancellationToken token)
    {
        var gate = await locks.TicketAsync(id, token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var config = await db.TicketConfigurations.SingleAsync(value => value.GuildId == guildId, token);
            auditCurrent = auditCurrent && config.LastAuditScanAtUtc >= config.CaptureFromUtc;

            var ticket = await db.Tickets
                .SingleAsync(value => value.GuildId == guildId && value.Id == id, token);

            switch (ticket.Action)
            {
                case TicketAction.Ban:
                    await expiry.ReconcileBanAsync(db, ticket, auditCurrent, token);
                    break;
                case TicketAction.Timeout:
                    await timeouts.ReconcileAsync(db, ticket, auditCurrent, token);
                    break;
                case TicketAction.Role:
                    await expiry.ReconcileRoleAsync(db, ticket, auditCurrent, token);
                    break;
            }

            await db.SaveChangesAsync(token);
        }
        finally
        {
            gate.Dispose();
        }
    }

    internal async Task ScheduleTimeoutChecksAsync(ulong guildId, ulong userId, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);

        IQueryable<Ticket> tickets = db.Tickets
            .Where(ticket => ticket.GuildId == guildId
                       && (userId == 0 || ticket.SubjectUserId == userId)
                       && ticket.Action == TicketAction.Timeout
                       && ticket.Status == TicketStatus.Active);

        List<long> ids = await tickets
            .Select(ticket => ticket.Id)
            .ToListAsync(token);

        foreach (var id in ids)
        {
            using var gate = await locks.TicketAsync(id, token);

            await tickets
                .Where(ticket => ticket.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(ticket => ticket.TimeoutCheckAtUtc, DateTimeOffset.UtcNow), token);
        }
    }

    internal async Task SuspendRestrictionsAsync(ulong guildId, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);

        IQueryable<Ticket> tickets = db.Tickets
            .Where(ticket => ticket.GuildId == guildId
                       && (ticket.Action == TicketAction.Ban
                           || ticket.Action == TicketAction.Timeout
                           || ticket.Action == TicketAction.Role)
                       && !ticket.ExpirySuspended
                       && ticket.Status == TicketStatus.Active);

        List<long> ids = await tickets
            .Select(ticket => ticket.Id)
            .ToListAsync(token);

        foreach (var id in ids)
        {
            using var gate = await locks.TicketAsync(id, token);

            await tickets
                .Where(ticket => ticket.Id == id)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(ticket => ticket.ExpirySuspended, true)
                        .SetProperty(ticket => ticket.Version, ticket => ticket.Action == TicketAction.Timeout ? ticket.Version + 1 : ticket.Version),
                    token);
        }
    }

    internal static void RequireStaff(TicketActor actor, bool hidden)
    {
        var staff = actor.IsAdministrator
            || actor.CanViewAuditLog
            || actor.CanTimeoutMembers
            || actor.CanBanMembers
            || actor.CanKickMembers
            || actor.CanMuteMembers
            || actor.CanDeafenMembers
            || actor.CanManageRoles;

        if (actor.UserId == 0 || actor.GuildId == 0 || !staff || (hidden && actor is { IsAdministrator: false, CanViewAuditLog: false }))
            throw new BotCommandException("Staff access is required; hidden tickets require View Audit Log or Administrator.");
    }

    private async Task PrepareNoteAsync(BotDbContext db, TicketActor actor, ulong subject, CancellationToken token)
    {
        if (subject == 0 || actor.UserId == 0 || actor is { IsAdministrator: false, CanViewAuditLog: false, CanTimeoutMembers: false })
            throw new BotCommandException("Creating notes requires Moderate Members or Administrator and a valid subject.");

        await EnabledAsync(db, actor.GuildId, token);
    }

    private static string Queued(string response, bool changed) => changed
        ? response + "\nChannel updates are queued."
        : response;

    private async Task<IDisposable> LockTicketAsync(
        ulong guildId,
        long number,
        CancellationToken token,
        [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        await using var db = await contexts.CreateDbContextAsync(token);

        var id = await db.Tickets
                .Where(ticket => ticket.GuildId == guildId && ticket.Number == number)
                .Select(ticket => (long?)ticket.Id)
                .SingleOrDefaultAsync(token)
            ?? throw new BotCommandException("No matching ticket in this server.");

        return await locks.TicketAsync(id, token, operation);
    }

    private async Task<Ticket> FindAsync(BotDbContext db, TicketActor actor, long number, CancellationToken token)
    {
        if (number <= 0)
            throw new BotCommandException("Supply a positive ticket number.");

        var ticket = await db.Tickets.SingleOrDefaultAsync(value => value.GuildId == actor.GuildId && value.Number == number, token)
            ?? throw new BotCommandException("No matching ticket in this server.");

        RequireStaff(actor, ticket.Hidden);
        await EnabledAsync(db, actor.GuildId, token);

        return ticket;
    }

    private async Task SaveAsync(BotDbContext db, CancellationToken token)
    {
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Error("Ticket save could not be confirmed ({FailureType})", exception.GetType().Name);

            throw new BotCommandException("Saving could not be confirmed. Read the saved ticket before retrying.", exception);
        }
    }
}