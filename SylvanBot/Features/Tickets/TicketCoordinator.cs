namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RolePersistence;

public sealed partial class TicketCoordinator(
    IDbContextFactory<BotDbContext> contexts,
    DiscordSocketClient client,
    NoteTicketStrategy notes,
    BanTicketStrategy bans,
    TimeoutTicketStrategy timeouts,
    KickTicketStrategy kicks,
    VoiceMuteTicketStrategy voiceMutes,
    VoiceDeafenTicketStrategy voiceDeafens,
    RoleTicketStrategy roles,
    RolePersistenceWorker persistence,
    TicketBoundary boundary,
    TicketPublisher publisher,
    ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    private async Task WaitForGateAsync(CancellationToken token, [CallerMemberName] string operation = "")
    {
        using var timing = new OperationTiming(logger.ForContext("ticket_operation", operation), "ticket.gate", "wait");
        await _gate.WaitAsync(token);
    }

    public async Task<TicketResult> CreateAsync(TicketActor actor, ulong subject, string comment, CancellationToken token)
    {
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            await PrepareNoteAsync(db, actor, subject, token);
            var result = await notes.HandleAsync(db, new CreateNote(actor, subject, comment), token);

            return await PublishAsync(actor.GuildId, result, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PrepareNoteAsync(TicketActor actor, ulong subject, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        await PrepareNoteAsync(db, actor, subject, token);
    }

    public async Task<TicketResult> EditAsync(
        TicketActor actor,
        long number,
        int? version,
        TicketDetails details,
        bool? hidden,
        CancellationToken token)
    {
        actor = await boundary.GetActorAsync(actor.GuildId, actor.UserId, token);
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);

            var ticket = await FindAsync(db, actor, number, token);
            var originalVersion = ticket.Version;
            var outcome = string.Empty;
            var hasDetails = details.HasDuration || details.Comment.Value is not TicketCommentEdit.Keep;

            if (hidden is not null && hasDetails && ticket.Action != TicketAction.Note)
                throw new BotCommandException("Change visibility separately from ticket details.");

            if (hasDetails)
            {
                ticket.RequireEdit(actor, version ?? ticket.Version);
                ticket.ValidateEdit(details, DateTimeOffset.UtcNow);
            }

            if (hidden is { } visibility)
            {
                ticket.RequireVisibilityChange(actor, version ?? ticket.Version);

                if (visibility && ticket.Status == TicketStatus.Active)
                    await RevertAsync(db, actor, ticket, ticket.Version, token);

                ticket.SetHidden(actor, ticket.Version, visibility);
            }

            if (hidden is null || hasDetails)
            {
                if (hidden is null)
                    ticket.RequireEdit(actor, version ?? ticket.Version);

                var comment = ticket.ValidateEdit(details, DateTimeOffset.UtcNow);

                if (details.HasDuration)
                    outcome = (await Strategy(ticket.Action).HandleAsync(db, new EditDetails(ticket, actor, details), token)).Outcome;

                if (details.Comment.Value is not TicketCommentEdit.Keep)
                    ticket.SetComment(comment, actor.UserId, details.Comment.Value is TicketCommentEdit.Append);
            }

            await SaveAsync(db, token);

            ImmutableArray<long> ids = ticket.Version == originalVersion ? ImmutableArray<long>.Empty : [ticket.Id];

            var text = ids.IsEmpty ? $"Ticket #{number}: nothing changed." : $"Saved ticket #{number}.";

            return await PublishAsync(actor.GuildId, new TicketResult(ids, text + (outcome.Length > 0 ? "\n" + outcome : string.Empty)), token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TicketResult> RevertAsync(TicketActor actor, long number, int? version, CancellationToken token)
    {
        actor = await boundary.GetActorAsync(actor.GuildId, actor.UserId, token);
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var ticket = await FindAsync(db, actor, number, token);
            await RevertAsync(db, actor, ticket, version ?? ticket.Version, token);

            return await PublishAsync(actor.GuildId, new TicketResult([ticket.Id], $"Reverted ticket #{number}. The record is retained."), token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RevertAsync(BotDbContext db, TicketActor actor, Ticket ticket, int version, CancellationToken token)
    {
        ticket.RequireEdit(actor, version);

        if (ticket.Action is TicketAction.Note or TicketAction.Kick)
            throw new BotCommandException("Notes and kicks have no active restriction to revert.");

        if (ticket.Status != TicketStatus.Active)
            throw new BotCommandException("This ticket no longer has an active restriction.");

        var permitted = actor.IsAdministrator
            || ticket.Action switch
            {
                TicketAction.Ban => actor.CanBanMembers,
                TicketAction.Timeout => actor.CanTimeoutMembers,
                TicketAction.VoiceMute => actor.CanMuteMembers,
                TicketAction.VoiceDeafen => actor.CanDeafenMembers,
                TicketAction.Role => actor.CanManageRoles,
                _ => false,
            };

        if (!permitted)
            throw new BotCommandException("You need the Discord permission for this restriction to revert it.");

        var config = await EnabledAsync(db, actor.GuildId, token);
        DateTimeOffset? from = config.LastAuditScanAtUtc;

        if (ticket.SourceAuditId is null || from is null || from < DateTimeOffset.UtcNow.AddDays(-45))
            throw new BotCommandException("Refresh after audit reconciliation before reverting this restriction.");

        var scan = await boundary.ReadAuditAsync(actor.GuildId, from.Value.AddMinutes(-10), token);

        var newer = scan.Observations.Any(value => value.Action == ticket.Action
                                              && value.SubjectUserId == ticket.SubjectUserId
                                              && value.RoleId == ticket.RoleId
                                              && value.AuditId > ticket.LastAuditId);

        if (newer)
            throw new BotCommandException("A newer change awaits audit reconciliation. Refresh before reverting.");

        var roleGate = ticket.Action == TicketAction.Role ? persistence.Gate(actor.GuildId) : null;

        if (roleGate is not null)
            await roleGate.WaitAsync(token);

        try
        {
            await RevertRestrictionAsync(actor, ticket, token);
            ticket.CompleteStaffReversal(actor.UserId, DateTimeOffset.UtcNow);

            if (ticket.Action == TicketAction.Role)
            {
                await db.PendingRoles
                   .Where(value => value.GuildId == actor.GuildId
                              && value.UserId == ticket.SubjectUserId
                              && value.RoleId == ticket.RoleId)
                   .ExecuteDeleteAsync(token);
            }

            await SaveAsync(db, token);
        }
        finally
        {
            roleGate?.Release();
        }
    }

    private async Task RevertRestrictionAsync(TicketActor actor, Ticket ticket, CancellationToken token)
    {
        var options = TicketBoundary.Options(token);
        options.AuditLogReason = $"Revert Sylvan ticket {ticket.Number}; moderator {actor.UserId}";
        var guild = await client.Rest.GetGuildAsync(actor.GuildId, options);
        var moderator = await guild.GetUserAsync(actor.UserId, options);
        var member = await guild.GetUserAsync(ticket.SubjectUserId, options);
        var bot = await guild.GetCurrentUserAsync(options);

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

        var voiceMemberMissing = member is null && ticket.Action is TicketAction.VoiceMute or TicketAction.VoiceDeafen;

        if (voiceMemberMissing)
            throw new BotCommandException("The member must be in the server to revert this restriction.");

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
                case TicketAction.VoiceMute:
                    await member!.ModifyAsync(properties => properties.Mute = false, options);
                    break;
                case TicketAction.VoiceDeafen:
                    await member!.ModifyAsync(properties => properties.Deaf = false, options);
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

    public async Task<TicketResult> ApproveAsync(TicketActor actor, long number, int version, CancellationToken token)
    {
        RequireStaff(actor, hidden: true);
        using var timing = new OperationTiming(logger.ForContext("TicketNumber", number), "ticket.approve", "gate");
        await WaitForGateAsync(token);

        try
        {
            timing.Next("database");
            await using var db = await contexts.CreateDbContextAsync(token);
            await EnabledAsync(db, actor.GuildId, token);

            var ticket = await db.Tickets
                   .SingleOrDefaultAsync(value => value.GuildId == actor.GuildId && value.Number == number, token)
                ?? throw new BotCommandException("No matching ticket in this server.");

            var previous = ticket.Version;
            ticket.Approve(actor, version);

            timing.Next("save");
            await SaveAsync(db, token);

            timing.Next("publish");

            return await PublishAsync(
                actor.GuildId,
                new TicketResult(previous == ticket.Version ? [] : [ticket.Id], $"Approved ticket #{number}."),
                token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Ticket?> ShowAsync(TicketActor actor, long number, bool hidden, CancellationToken token)
    {
        RequireStaff(actor, hidden);
        await using var db = await contexts.CreateDbContextAsync(token);
        await EnabledAsync(db, actor.GuildId, token);

        return await db.Tickets
           .AsNoTracking()
           .SingleOrDefaultAsync(ticket => ticket.GuildId == actor.GuildId && ticket.Number == number && ticket.Hidden == hidden, token);
    }

    public async Task<Ticket> PrepareEditAsync(TicketActor actor, long number, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var ticket = await FindAsync(db, actor, number, token);
        ticket.RequireEdit(actor, ticket.Version);

        return ticket;
    }

    public async Task<Ticket?> ShowMessageAsync(TicketActor actor, TicketMessage message, bool hidden, CancellationToken token)
    {
        RequireStaff(actor, hidden);
        await using var db = await contexts.CreateDbContextAsync(token);
        await EnabledAsync(db, actor.GuildId, token);

        var ticket = await db.Tickets
           .AsNoTracking()
           .Where(value => value.GuildId == actor.GuildId
                      && value.Hidden == hidden
                      && value.Publications.Any(reference
                                                    => reference.ChannelId == message.ChannelId && reference.MessageId == message.MessageId))
           .SingleOrDefaultAsync(token);

        return ticket;
    }

    public async Task<TicketPage> ListAsync(TicketActor actor, ulong subject, int page, bool hidden, CancellationToken token)
    {
        if (subject == 0 || page is < 1 or > 1_000_000)
            throw new BotCommandException("Supply a valid user and a page between 1 and 1,000,000.");

        await using var db = await contexts.CreateDbContextAsync(token);
        await EnabledAsync(db, actor.GuildId, token);
        RequireStaff(actor, hidden);
        var senior = actor.IsAdministrator || actor.CanViewAuditLog;

        List<Ticket> tickets = await db.Tickets
           .AsNoTracking()
           .Where(ticket => ticket.GuildId == actor.GuildId
                      && ticket.SubjectUserId == subject
                      && (!hidden || ticket.Hidden)
                      && (senior || !ticket.Hidden))
           .OrderByDescending(ticket => ticket.CreatedAtUtc)
           .ThenByDescending(ticket => ticket.Id)
           .Skip((page - 1) * 10)
           .Take(11)
           .ToListAsync(token);

        return new TicketPage(tickets.Take(10).ToImmutableArray(), page, tickets.Count > 10);
    }

    internal static IQueryable<Ticket> AwaitingApproval(IQueryable<Ticket> tickets, ulong guildId) =>
        tickets.Where(ticket => ticket.GuildId == guildId
                          && !ticket.Hidden
                          && !ticket.ReviewExempt
                          && ticket.ApprovedByUserId == null
                          && ticket.Action != TicketAction.Note);

    public async Task<(Ticket? Ticket, int Ready, int Waiting)> ReviewQueueAsync(TicketActor actor, long after, CancellationToken token)
    {
        RequireStaff(actor, hidden: true);
        using var timing = new OperationTiming(logger.ForContext("GuildId", actor.GuildId), "ticket.review_queue", "configuration");
        await using var db = await contexts.CreateDbContextAsync(token);
        await EnabledAsync(db, actor.GuildId, token);

        IQueryable<Ticket> pending = AwaitingApproval(db.Tickets, actor.GuildId);

        long cursor = 0;
        var ready = 0;
        var waiting = 0;
        Ticket? next = null;

        while (true)
        {
            var cursorAfter = cursor;
            timing.Next("database_batch");

            List<Ticket> batch = await pending
               .AsNoTracking()
               .Where(ticket => ticket.Number > cursorAfter)
               .OrderBy(ticket => ticket.Number)
               .Take(100)
               .ToListAsync(token);

            if (batch.Count == 0)
                break;

            timing.Next("counting");

            foreach (var ticket in batch)
            {
                if (ticket is { Complete: true, Pending: null })
                {
                    ready++;

                    if (next is null && ticket.Number > after)
                        next = ticket;
                }
                else
                {
                    waiting++;
                }
            }

            cursor = batch[^1].Number;
        }

        logger.Debug("Ticket review queue in guild {GuildId}: {ReadyCount} ready, {WaitingCount} waiting", actor.GuildId, ready, waiting);

        return (next, ready, waiting);
    }

    public async Task<TicketConfiguration> ConfigureAsync(
        TicketActor actor,
        bool? enabled,
        ulong? channelId,
        int? reminderIntervalSeconds,
        CancellationToken cancellationToken,
        ulong? exemptRoleAdd = null,
        ulong? exemptRoleRemove = null,
        ulong? trackedRoleAdd = null,
        ulong? trackedRoleRemove = null)
    {
        if (actor.GuildId == 0 || actor.UserId == 0 || !actor.IsAdministrator)
            throw new BotCommandException("Administrator permission is required to configure tickets.");

        await WaitForGateAsync(cancellationToken);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);

            var config = await db.TicketConfigurations
               .SingleOrDefaultAsync(value => value.GuildId == actor.GuildId, cancellationToken);

            if (config is null)
            {
                config = new TicketConfiguration { GuildId = actor.GuildId };
                db.TicketConfigurations.Add(config);
            }

            var destination = channelId ?? config.DestinationChannelId;

            if (reminderIntervalSeconds < 60 || ((enabled ?? config.Enabled) && destination is null))
                throw new BotCommandException("Choose a tickets channel and a reminder interval of at least one minute.");

            if (enabled == true)
                await boundary.CheckCaptureAsync(actor.GuildId, cancellationToken);

            if (enabled == true && (!config.Enabled || config.CaptureFromUtc is null))
                config.CaptureFromUtc = DateTimeOffset.UtcNow;

            if (enabled is { } active && active != config.Enabled)
            {
                IQueryable<Ticket> tickets = db.Tickets;

                List<TicketModerator> moderators = await db.TicketModerators
                   .Where(moderator => tickets.Any(ticket =>
                                                       ticket.GuildId == actor.GuildId
                                                       && ticket.Id == moderator.ActiveTicketId))
                   .ToListAsync(cancellationToken);

                foreach (var moderator in moderators)
                {
                    moderator.NextDeliveryUtc = null;
                    moderator.PromptVersion = null;
                    moderator.Version++;
                }
            }

            config.Enabled = enabled ?? config.Enabled;
            config.DestinationChannelId = destination;
            config.ReminderIntervalSeconds = reminderIntervalSeconds ?? config.ReminderIntervalSeconds;
            config.UpdateReviewExemptRoles(exemptRoleAdd, exemptRoleRemove);

            if (trackedRoleAdd is { } added && !config.TrackedRoleIds.Contains(checked((long)added)))
                config.TrackedRoleIds.Add(checked((long)added));

            if (trackedRoleRemove is { } removed)
                config.TrackedRoleIds.Remove(checked((long)removed));

            await SaveAsync(db, cancellationToken);

            return config;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemindAsync(ulong userId, bool force, CancellationToken token)
    {
        await WaitForGateAsync(token);

        try
        {
            await publisher.RemindAsync(userId, force, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RedeliverAsync(TicketActor actor, CancellationToken token)
    {
        actor = await boundary.GetActorAsync(actor.GuildId, actor.UserId, token);
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            await EnabledAsync(db, actor.GuildId, token);
            RequireStaff(actor, hidden: false);
            await publisher.RemindAsync(actor.UserId, true, token, actor.GuildId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    internal static async Task<TicketConfiguration> EnabledAsync(BotDbContext db, ulong guildId, CancellationToken token)
    {
        var config = await db.TicketConfigurations
           .SingleOrDefaultAsync(value => value.GuildId == guildId, token);

        return config is { Enabled: true, DestinationChannelId: not null }
            ? config
            : throw new BotCommandException("Tickets are not enabled in this server.");
    }

    internal async Task AcceptScanAsync(TicketConfiguration scannedConfig, TicketAuditScan scan, DateTimeOffset started, CancellationToken token)
    {
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var config = await EnabledAsync(db, scannedConfig.GuildId, token);

            if (config.CaptureFromUtc != scannedConfig.CaptureFromUtc)
                return;

            Dictionary<Guid, ulong> correlations = [];

            foreach (var value in scan.Observations)
                if (value.OperationId.TryGet(out var correlation))
                    correlations[correlation] = Math.Max(correlations.GetValueOrDefault(correlation), value.AuditId);

            IEnumerable<IGrouping<ulong, TicketObservation>> groups = scan.Observations.GroupBy(observation => observation.AuditId);

            foreach (IGrouping<ulong, TicketObservation> group in groups)
            {
                List<long> affected = [];

                await using (var transaction = await db.Database.BeginTransactionAsync(token))
                {
                    foreach (var observation in group)
                    {
                        var member = config.ReviewExemptRoleIds.Count == 0
                            ? null
                            : await client.Rest.GetGuildUserAsync(config.GuildId, observation.ModeratorUserId, TicketBoundary.Options(token));

                        var exempt = member is not null
                            && (config.ReviewExemptRoleIds.Contains(checked((long)config.GuildId))
                                || member.RoleIds.Any(id => config.ReviewExemptRoleIds.Contains(checked((long)id))));

                        var result = await Strategy(observation.Action)
                           .HandleAsync(db, new ObserveModeration(observation, exempt, correlations), token);

                        affected.AddRange(result.TicketIds);
                    }

                    config.LastAuditId = Math.Max(config.LastAuditId ?? 0, group.Key);
                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                }

                await PublishAsync(config.GuildId, new TicketResult(affected.ToImmutableArray(), string.Empty), token);
                db.ChangeTracker.Clear();
                db.Attach(config);
            }

            config.LastAuditId = Math.Max(config.LastAuditId ?? 0, scan.LastAuditId.TryGet(out var last) ? last : 0);
            config.LastAuditScanAtUtc = started;
            await db.SaveChangesAsync(token);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task RecoverAsync(ulong guildId, long id, bool auditCurrent, CancellationToken token)
    {
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var config = await EnabledAsync(db, guildId, token);
            auditCurrent = auditCurrent && config.LastAuditScanAtUtc >= config.CaptureFromUtc;

            var ticket = await db.Tickets
               .SingleAsync(value => value.GuildId == guildId && value.Id == id, token);

            if (ticket.Action is not (TicketAction.Note or TicketAction.Kick))
                await Strategy(ticket.Action).HandleAsync(db, new ReconcileTicket(ticket, auditCurrent), token);

            await db.SaveChangesAsync(token);
            await publisher.EnsureAsync(guildId, id, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<bool> PublishOrderedBatchAsync(TicketConfiguration configuration, CancellationToken token)
    {
        const int batchSize = 25;
        var guildId = configuration.GuildId;
        await using var db = await contexts.CreateDbContextAsync(token);

        List<long> candidates = await db.Tickets.AsNoTracking()
           .Where(ticket => ticket.GuildId == guildId
                      && !ticket.Hidden
                      && (configuration.LegacyChannelId == null || ticket.ImportedFromLegacy)
                      && !ticket.Publications.Any(copy => copy.Current
                                                      && !copy.Removed
                                                      && (ticket.PinnedPublicationChannelId == null
                                                          || copy.ChannelId == ticket.PinnedPublicationChannelId)))
           .OrderByDescending(ticket => configuration.LegacyChannelId != null
                                  && ticket.PinnedPublicationChannelId == configuration.DestinationChannelId)
           .ThenBy(ticket => ticket.Number)
           .Select(ticket => ticket.Id)
           .Take(batchSize)
           .ToListAsync(token);

        if (candidates.Count == 0)
        {
            await WaitForGateAsync(token);

            try
            {
                // A gateway or staff command may have saved another ticket since the first read.
                var config = await EnabledAsync(db, guildId, token);

                if (config.OrderedPublicationPaused)
                    return false;

                if (await db.Tickets.AnyAsync(
                    ticket => ticket.GuildId == guildId
                        && !ticket.Hidden
                        && (config.LegacyChannelId == null || ticket.ImportedFromLegacy)
                        && !ticket.Publications.Any(copy => copy.Current
                                                        && !copy.Removed
                                                        && (ticket.PinnedPublicationChannelId == null
                                                            || copy.ChannelId == ticket.PinnedPublicationChannelId)),
                    token))
                    return true;

                config.OrderedPublicationPending = false;
                await db.SaveChangesAsync(token);
                return false;
            }
            finally
            {
                _gate.Release();
            }
        }

        foreach (var id in candidates)
        {
            await WaitForGateAsync(token);

            try
            {
                await publisher.EnsureAsync(guildId, id, token, orderedPublication: true);

                // The send may have failed or reached Discord before its reference was saved.
                // Keep later numbers behind this one until its publication is accounted for.
                if (!await db.Tickets.AnyAsync(
                    ticket => ticket.GuildId == guildId
                        && ticket.Id == id
                        && (ticket.Hidden
                            || ticket.Publications.Any(copy => copy.Current
                                                           && !copy.Removed
                                                           && (ticket.PinnedPublicationChannelId == null
                                                               || copy.ChannelId == ticket.PinnedPublicationChannelId))),
                    token))
                    return false;
            }
            finally
            {
                _gate.Release();
            }
        }

        return true;
    }

    internal async Task ScheduleTimeoutChecksAsync(ulong guildId, ulong userId, CancellationToken token)
    {
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);

            await db.Tickets
               .Where(ticket => ticket.GuildId == guildId
                          && (userId == 0 || ticket.SubjectUserId == userId)
                          && ticket.Action == TicketAction.Timeout
                          && ticket.Status == TicketStatus.Active)
               .ExecuteUpdateAsync(update => update.SetProperty(ticket => ticket.TimeoutCheckAtUtc, DateTimeOffset.UtcNow), token);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SuspendRestrictionsAsync(ulong guildId, CancellationToken token)
    {
        await WaitForGateAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);

            await db.Tickets
               .Where(ticket => ticket.GuildId == guildId
                          && (ticket.Action == TicketAction.Ban
                              || ticket.Action == TicketAction.Timeout
                              || ticket.Action == TicketAction.VoiceMute
                              || ticket.Action == TicketAction.VoiceDeafen
                              || ticket.Action == TicketAction.Role)
                          && !ticket.ExpirySuspended
                          && ticket.Status == TicketStatus.Active)
               .ExecuteUpdateAsync(
                    update => update
                       .SetProperty(ticket => ticket.ExpirySuspended, true)
                       .SetProperty(
                            ticket => ticket.Version,
                            ticket => ticket.Action == TicketAction.Timeout ? ticket.Version + 1 : ticket.Version),
                    token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RequireStaff(TicketActor actor, bool hidden)
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

    private ITicketStrategy Strategy(TicketAction action) => action switch
    {
        TicketAction.Note => notes,
        TicketAction.Ban => bans,
        TicketAction.Timeout => timeouts,
        TicketAction.Kick => kicks,
        TicketAction.VoiceMute => voiceMutes,
        TicketAction.VoiceDeafen => voiceDeafens,
        TicketAction.Role => roles,
        _ => throw new BotCommandException("This ticket action has no operational strategy yet."),
    };

    private async Task<TicketResult> PublishAsync(ulong guildId, TicketResult result, CancellationToken token)
    {
        var outcome = result.Outcome;

        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        // this doesnt WORK for async methods u fuckin DOLT
        foreach (var id in result.TicketIds)
            outcome += await publisher.EnsureAsync(guildId, id, token);

        return result with { Outcome = outcome };
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
