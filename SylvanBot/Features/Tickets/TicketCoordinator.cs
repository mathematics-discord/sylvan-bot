namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketCoordinator(
    IDbContextFactory<BotDbContext> contexts,
    DiscordSocketClient client,
    NoteTicketStrategy notes,
    BanTicketStrategy bans,
    TimeoutTicketStrategy timeouts,
    KickTicketStrategy kicks,
    VoiceMuteTicketStrategy voiceMutes,
    VoiceDeafenTicketStrategy voiceDeafens,
    RoleTicketStrategy roles,
    TicketBoundary boundary,
    TicketPublisher publisher,
    ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    public async Task<TicketResult> CreateAsync(TicketActor actor, ulong subject, string comment, CancellationToken token)
    {
        await _gate.WaitAsync(token);

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
        await _gate.WaitAsync(token);

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
                ticket.SetHidden(actor, version ?? ticket.Version, visibility);

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

    public async Task<TicketResult> ApproveAsync(TicketActor actor, long number, int version, CancellationToken token)
    {
        actor = await boundary.GetActorAsync(actor.GuildId, actor.UserId, token);
        await _gate.WaitAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);

            var ticket = await FindAsync(db, actor, number, token);
            var previous = ticket.Version;
            ticket.Approve(actor, version);

            await SaveAsync(db, token);

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
        await using var db = await contexts.CreateDbContextAsync(token);
        var ticket = await FindAsync(db, actor, number, token);

        return ticket.Hidden == hidden ? ticket : null;
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
        await using var db = await contexts.CreateDbContextAsync(token);
        await EnabledAsync(db, actor.GuildId, token);
        await CheckReadAsync(actor, message.ChannelId, token);

        var ticket = await db.Tickets
           .Where(value => value.GuildId == actor.GuildId
                      && value.Hidden == hidden
                      && value.Publications.Any(reference
                                                    => reference.ChannelId == message.ChannelId && reference.MessageId == message.MessageId))
           .SingleOrDefaultAsync(token);

        if (ticket is not null)
            await CheckTicketAsync(db, actor, ticket, token);

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
           .Where(ticket => ticket.GuildId == actor.GuildId
                      && ticket.SubjectUserId == subject
                      && (!hidden || ticket.Hidden)
                      && (senior || !ticket.Hidden))
           .OrderByDescending(ticket => ticket.CreatedAtUtc)
           .ThenByDescending(ticket => ticket.Id)
           .Skip((page - 1) * 10)
           .Take(11)
           .ToListAsync(token);

        foreach (var ticket in tickets)
            await CheckTicketAsync(db, actor, ticket, token);

        return new TicketPage(tickets.Take(10).ToImmutableArray(), page, tickets.Count > 10);
    }

    public async Task<(Ticket? Ticket, int Ready, int Waiting)> ReviewQueueAsync(TicketActor actor, long after, CancellationToken token)
    {
        actor = await boundary.GetActorAsync(actor.GuildId, actor.UserId, token);
        RequireStaff(actor, hidden: true);
        await using var db = await contexts.CreateDbContextAsync(token);
        await EnabledAsync(db, actor.GuildId, token);

        IQueryable<Ticket> pending = db.Tickets
           .Where(ticket => ticket.GuildId == actor.GuildId
                      && !ticket.Hidden
                      && !ticket.ReviewExempt
                      && ticket.ApprovedByUserId == null
                      && ticket.Action != TicketAction.Note);

        long cursor = 0;
        var ready = 0;
        var waiting = 0;
        Ticket? next = null;

        while (true)
        {
            var cursorAfter = cursor;

            List<Ticket> batch = await pending
               .Where(ticket => ticket.Number > cursorAfter)
               .OrderBy(ticket => ticket.Number)
               .Take(100)
               .ToListAsync(token);

            if (batch.Count == 0)
                break;

            foreach (var ticket in batch)
            {
                try
                {
                    await CheckTicketAsync(db, actor, ticket, token);
                }
                catch (BotCommandException)
                {
                    continue;
                }

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
            db.ChangeTracker.Clear();
        }

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

        await _gate.WaitAsync(cancellationToken);

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

            if (destination is { } channel)
                await CheckReadAsync(actor, channel, cancellationToken);

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
        await _gate.WaitAsync(token);

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
        await _gate.WaitAsync(token);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var config = await EnabledAsync(db, actor.GuildId, token);
            RequireStaff(actor, hidden: false);
            await CheckReadAsync(actor, config.DestinationChannelId!.Value, token);
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
        await _gate.WaitAsync(token);

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
        await _gate.WaitAsync(token);

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

    internal async Task ScheduleTimeoutChecksAsync(ulong guildId, ulong userId, CancellationToken token)
    {
        await _gate.WaitAsync(token);

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
        await _gate.WaitAsync(token);

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

        var config = await EnabledAsync(db, actor.GuildId, token);
        await CheckReadAsync(actor, config.DestinationChannelId!.Value, token);
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

        foreach (var id in result.TicketIds)
            outcome += await publisher.EnsureAsync(guildId, id, token);

        return result with { Outcome = outcome };
    }

    private async Task<Ticket> FindAsync(BotDbContext db, TicketActor actor, long number, CancellationToken token)
    {
        if (number <= 0)
            throw new BotCommandException("Supply a positive ticket number.");

        var ticket = await db.Tickets
               .SingleOrDefaultAsync(value => value.GuildId == actor.GuildId && value.Number == number, token)
            ?? throw new BotCommandException("No matching ticket in this server.");

        await CheckTicketAsync(db, actor, ticket, token);

        return ticket;
    }

    private async Task CheckTicketAsync(BotDbContext db, TicketActor actor, Ticket ticket, CancellationToken token)
    {
        RequireStaff(actor, ticket.Hidden);
        var config = await EnabledAsync(db, actor.GuildId, token);

        var channel = ticket.Publications
               .FirstOrDefault(reference => reference is { Current: true, Removed: false })
              ?.ChannelId
            ?? config.DestinationChannelId!.Value;

        await CheckReadAsync(actor, channel, token);
    }

    private async Task CheckReadAsync(TicketActor actor, ulong channelId, CancellationToken token)
    {
        var guild = await client.Rest.GetGuildAsync(actor.GuildId, TicketBoundary.Options(token));
        var channel = await guild.GetTextChannelAsync(channelId, options: TicketBoundary.Options(token));
        var member = await guild.GetUserAsync(actor.UserId, TicketBoundary.Options(token));

        if (channel is null || member?.GetPermissions(channel).ViewChannel != true)
            throw new BotCommandException("You must be able to view the tickets channel to access its records.");
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