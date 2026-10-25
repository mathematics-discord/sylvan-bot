namespace SylvanBot.Application.Features.Tickets;

using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Common;
using Infrastructure.Data;

public sealed class TicketWorkflow(
    IDbContextFactory<BotDbContext> contextFactory,
    IDiscordTicket discord,
    ILogger<TicketWorkflow> logger)
    : IDisposable
{
    private readonly SemaphoreSlim _publicationLock = new(1, 1);

    // Shared configuration, lookup and history.
    public async Task<TicketConfiguration> ConfigureAsync(
        TicketActor actor,
        bool? enabled,
        ulong? channelId,
        CancellationToken cancellationToken = default)
    {
        if (actor.GuildId == 0 || actor.UserId == 0 || !actor.IsAdministrator)
            throw new BotCommandException("Administrator permission is required to configure tickets.");

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var configuration = await dbContext.TicketConfigurations
            .SingleOrDefaultAsync(configuration => configuration.GuildId == actor.GuildId, cancellationToken);

        var isNew = configuration is null;
        configuration ??= new TicketConfiguration { GuildId = actor.GuildId };

        if (enabled is null && channelId is null)
            return configuration;

        var destination = channelId ?? configuration.DestinationChannelId;
        var updatedEnabled = enabled ?? configuration.Enabled;

        if (updatedEnabled && destination is null)
            throw new BotCommandException(
                "Choose a tickets channel before enabling notes. Configuration was not changed.");

        if (destination is not null && (updatedEnabled || channelId is not null))
            await discord.CheckChannelAsync(actor, destination.Value, publishing: true, cancellationToken);

        configuration.Enabled = updatedEnabled;
        configuration.DestinationChannelId = destination;

        if (isNew)
            dbContext.TicketConfigurations.Add(configuration);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Ticket configuration save in guild {GuildId} could not be confirmed ({FailureType})",
                actor.GuildId,
                exception.GetType().Name);

            throw new BotCommandException(
                "Saving ticket configuration could not be confirmed. Read /ticket-admin config before changing it again.",
                exception);
        }

        return configuration;
    }

    public async Task<Option<Ticket>> ShowAsync(
        TicketActor actor,
        long number,
        bool hidden = false,
        CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        RequireHiddenAccess(actor, hidden);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: false, cancellationToken);

        if (ticket is null || ticket.Hidden != hidden)
            return new None();

        await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

        return new Some<Ticket>(ticket);
    }

    public async Task<Option<Ticket>> ShowMessageAsync(
        TicketActor actor,
        TicketMessage publication,
        bool hidden = false,
        CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        RequireHiddenAccess(actor, hidden);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.GuildId == actor.GuildId && ticket.Hidden == hidden)
            .SingleOrDefaultAsync(
                ticket =>
                    (ticket.ChannelId == publication.ChannelId && ticket.MessageId == publication.MessageId)
                    || (ticket.PreviousChannelId == publication.ChannelId
                        && ticket.PreviousMessageId == publication.MessageId),
                cancellationToken
            );

        if (ticket is null)
            return new None();

        await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

        return new Some<Ticket>(ticket);
    }

    public async Task<TicketPage> ListAsync(
        TicketActor actor,
        ulong subjectUserId,
        int page = 1,
        bool hidden = false,
        CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        RequireHiddenAccess(actor, hidden);

        if (subjectUserId == 0)
            throw new BotCommandException("Provide a nonzero subject user ID.");

        if (page is < 1 or > 1_000_000)
            throw new BotCommandException("Choose a ticket history page between 1 and 1,000,000.");

        var canViewHidden = actor.IsAdministrator || actor.CanViewAuditLog;
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<Ticket> tickets = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket =>
                ticket.GuildId == actor.GuildId
                && ticket.SubjectUserId == subjectUserId
                && (!hidden || ticket.Hidden)
                && (canViewHidden || !ticket.Hidden)
            )
            .OrderByDescending(ticket => ticket.CreatedAtUtc)
            .ThenByDescending(ticket => ticket.Id)
            .Skip((page - 1) * 10)
            .Take(11)
            .ToListAsync(cancellationToken);

        ImmutableArray<Ticket> visiblePage = tickets
            .Take(10)
            .ToImmutableArray();

        foreach (var ticket in visiblePage)
            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

        return new TicketPage(visiblePage, page, tickets.Count > 10);
    }

    // Note creation and correction. Other ticket types remain read-only.
    public async Task<ulong> PrepareCreateNoteAsync(
        TicketActor actor,
        ulong subjectUserId,
        CancellationToken cancellationToken = default)
    {
        RequireNotePermission(actor);

        if (subjectUserId == 0)
            throw new BotCommandException("Provide a nonzero subject user ID. No note was created.");

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var configuration = await dbContext.TicketConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.GuildId == actor.GuildId, cancellationToken);

        if (configuration is not { Enabled: true })
            throw new BotCommandException("Note creation is disabled in this server. No note was created.");

        if (configuration.DestinationChannelId is null)
            throw new BotCommandException("Configure a tickets channel before creating notes. No note was created.");

        await discord.CheckChannelAsync(
            actor,
            configuration.DestinationChannelId.Value,
            publishing: true,
            cancellationToken);

        return configuration.DestinationChannelId.Value;
    }

    public async Task<TicketChange> CreateNoteAsync(
        TicketActor actor,
        ulong subjectUserId,
        string comment,
        CancellationToken cancellationToken = default)
    {
        ValidateNoteComment(comment);
        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            var channelId = await PrepareCreateNoteAsync(actor, subjectUserId, cancellationToken);
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var number = await dbContext.Database
                .SqlQuery<long>($"SELECT nextval('ticket_numbers') AS \"Value\"")
                .SingleAsync(cancellationToken);

            var ticket = new Ticket
            {
                GuildId = actor.GuildId,
                Number = number,
                Action = TicketAction.Note,
                SubjectUserId = subjectUserId,
                ModeratorUserId = actor.UserId,
                Comment = comment,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                PublicationPending = true,
            };

            dbContext.Tickets.Add(ticket);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                LogFailure(ticket, "saving new record", exception);

                throw new BotCommandException(
                    $"Saving ticket #{ticket.Number} could not be confirmed. No publication was attempted. "
                    + $"Check /ticket show id:{ticket.Number} before creating another note.",
                    exception);
            }

            return await PublishAsync(dbContext, ticket, channelId, cancellationToken);
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    public async Task<Ticket> PrepareNoteEditAsync(
        TicketActor actor,
        long number,
        CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: false, cancellationToken)
                     ?? throw new BotCommandException("No matching ticket in this server.");

        RequireNoteMutation(actor, ticket);
        await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

        return ticket;
    }

    public async Task<Ticket> PrepareNoteEditAsync(
        TicketActor actor,
        TicketMessage publication,
        CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var ticket = await dbContext.Tickets
                         .AsNoTracking()
                         .Where(ticket => ticket.GuildId == actor.GuildId)
                         .SingleOrDefaultAsync(
                             ticket =>
                                 (ticket.ChannelId == publication.ChannelId
                                  && ticket.MessageId == publication.MessageId)
                                 || (ticket.PreviousChannelId == publication.ChannelId
                                     && ticket.PreviousMessageId == publication.MessageId),
                             cancellationToken)
                     ?? throw new BotCommandException("No ticket in this server is associated with that message.");

        RequireNoteMutation(actor, ticket);
        await discord.CheckChannelAsync(actor, publication.ChannelId, publishing: false, cancellationToken);
        await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

        return ticket;
    }

    public Task<TicketChange> SetNoteAsync(
        TicketActor actor,
        long number,
        string? duration = null,
        string? comment = null,
        bool? hidden = null,
        int? expectedVersion = null,
        CancellationToken cancellationToken = default)
        => CorrectNoteAsync(
            actor,
            number,
            comment,
            duration,
            append: false,
            hidden,
            expectedVersion,
            cancellationToken);

    public Task<TicketChange> AppendNoteAsync(
        TicketActor actor,
        long number,
        string comment,
        CancellationToken cancellationToken = default)
        => CorrectNoteAsync(
            actor,
            number,
            comment,
            duration: null,
            append: true,
            hidden: null,
            expectedVersion: null,
            cancellationToken);

    public Task<TicketChange> HideNoteAsync(
        TicketActor actor,
        long number,
        CancellationToken cancellationToken = default)
        => CorrectNoteAsync(
            actor,
            number,
            comment: null,
            duration: null,
            append: false,
            hidden: true,
            expectedVersion: null,
            cancellationToken);

    private async Task<TicketChange> CorrectNoteAsync(
        TicketActor actor,
        long number,
        string? comment,
        string? duration,
        bool append,
        bool? hidden,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireStaff(actor);
        await _publicationLock.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: true, cancellationToken)
                         ?? throw new BotCommandException("No matching ticket in this server.");

            RequireNoteMutation(actor, ticket);
            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

            if (duration is not null)
                throw new BotCommandException("Notes do not accept a duration. Nothing was changed.");

            if (expectedVersion is not null && ticket.Version != expectedVersion)
                throw new BotCommandException(
                    "This note changed since you opened it. Your changes were not saved. Read it again before editing.");

            if (hidden is null && comment is null)
                return new TicketChange(ticket, NoChange: true);

            if (append && !string.IsNullOrEmpty(ticket.Comment))
                comment = "\n" + comment;

            if (comment is not null)
                ValidateNoteComment(comment, append ? ticket.Comment : null);

            var updatedHidden = hidden ?? ticket.Hidden;
            var unhiding = ticket.Hidden && !updatedHidden;
            ulong? channelId = null;

            if (!updatedHidden)
                channelId = await CorrectionChannelAsync(dbContext, actor, ticket, cancellationToken);

            if (comment is not null)
                ticket.Comment = append ? ticket.Comment + comment : comment;

            ticket.Hidden = updatedHidden;
            ticket.Version++;
            ticket.LastModifiedByUserId = actor.UserId;
            ticket.LastModifiedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException dbException)
            {
                throw new BotCommandException(
                    "This note changed while you were editing it. Your changes were not saved. Read it again before editing.",
                    dbException);
            }
            catch (Exception exception)
            {
                LogFailure(ticket, "saving correction", exception);

                throw new BotCommandException(
                    $"Saving changes to ticket #{ticket.Number} could not be confirmed. "
                    + "No publication update or removal was attempted. "
                    + $"Check /ticket show id:{ticket.Number} or /ticket showhidden id:{ticket.Number} "
                    + "before repeating the change.",
                    exception);
            }

            if (hidden == true)
                return await RemoveCopiesAsync(ticket, cancellationToken);

            if (ticket.Hidden)
                return new TicketChange(ticket);

            return await PublishCorrectionAsync(dbContext, ticket, channelId!.Value, unhiding, cancellationToken);
        }
        finally
        {
            _publicationLock.Release();
        }
    }

    private static void RequireNotePermission(TicketActor actor)
    {
        var canManageNotes = actor.GuildId != 0
                             && actor.UserId != 0
                             && (actor.IsAdministrator || actor.CanTimeoutMembers);

        if (!canManageNotes)
            throw new BotCommandException(
                "Creating or changing notes requires Moderate Members or Administrator permission.");
    }

    private static void RequireNoteMutation(TicketActor actor, Ticket ticket)
    {
        RequireStaff(actor);
        RequireHiddenAccess(actor, ticket.Hidden);

        if (ticket.Action != TicketAction.Note)
            throw new BotCommandException("This ticket type is read-only. Only notes can be changed or hidden.");

        RequireNotePermission(actor);
    }

    private static void ValidateNoteComment(string comment, string? existingComment = null)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new BotCommandException("A note comment must contain non-whitespace text. Nothing was changed.");

        if (comment.Length > Ticket.CommentLimit - (existingComment?.Length ?? 0))
            throw new BotCommandException(
                $"Ticket comments cannot exceed {Ticket.CommentLimit:N0} characters, "
                + "including existing text and the newline when appending. "
                + "Nothing was changed.");
    }

    // Shared publication and message associations; callers own action-specific rules.
    private async Task<ulong> CorrectionChannelAsync(
        BotDbContext dbContext,
        TicketActor actor,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        var channelId = ticket.ChannelId;

        channelId ??= await dbContext.TicketConfigurations
            .Where(configuration => configuration.GuildId == actor.GuildId)
            .Select(configuration => configuration.DestinationChannelId)
            .SingleOrDefaultAsync(cancellationToken);

        if (channelId is null)
            throw new BotCommandException(
                "Configure a tickets channel before publishing a correction. Nothing was changed.");

        await discord.CheckChannelAsync(actor, channelId.Value, publishing: true, cancellationToken);

        return channelId.Value;
    }

    private async Task<TicketChange> PublishCorrectionAsync(
        BotDbContext dbContext,
        Ticket ticket,
        ulong channelId,
        bool unhiding,
        CancellationToken cancellationToken)
    {
        if (ticket.PublicationPending)
            return new TicketChange(ticket, ReferenceSaved: false, PublicationUncertain: true);

        if (ticket.ChannelId is not null && ticket.MessageId is not null)
        {
            var publication = new TicketMessage(ticket.ChannelId.Value, ticket.MessageId.Value);

            try
            {
                if (await discord.UpdateAsync(ticket, publication, cancellationToken))
                    return new TicketChange(ticket, publication);
            }
            catch (Exception exception)
            {
                LogFailure(ticket, "updating publication", exception);

                return new TicketChange(ticket, PublicationUncertain: true);
            }

            // Keep the earlier link when the current bot cannot edit the linked message.
            if (ticket.PreviousMessageId is not null && !unhiding)
                return new TicketChange(ticket, PublicationUncertain: true);

            if (ticket.PreviousMessageId is null)
            {
                ticket.PreviousChannelId = ticket.ChannelId;
                ticket.PreviousMessageId = ticket.MessageId;
            }
        }

        // Persist intent before sending. An uncertain send or failed reference save cannot cause
        // a later correction to create another publication, even after a restart.
        ticket.PublicationPending = true;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            LogFailure(ticket, "saving publication intent", exception);

            throw new BotCommandException(
                $"Ticket #{ticket.Number} was saved, but no new publication was attempted because saving its "
                + "publication state could not be confirmed. Do not repeat the change. "
                + $"Check /ticket show id:{ticket.Number} and the tickets channel.",
                exception);
        }

        return await PublishAsync(dbContext, ticket, channelId, cancellationToken);
    }

    private async Task<TicketChange> PublishAsync(
        BotDbContext dbContext,
        Ticket ticket,
        ulong channelId,
        CancellationToken cancellationToken)
    {
        TicketMessage publication;

        try
        {
            publication = await discord.PublishAsync(ticket, channelId, cancellationToken);
        }
        catch (Exception exception)
        {
            LogFailure(ticket, "publishing", exception);

            return new TicketChange(ticket, ReferenceSaved: false, PublicationUncertain: true);
        }

        ticket.ChannelId = publication.ChannelId;
        ticket.MessageId = publication.MessageId;
        ticket.PublicationPending = false;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return new TicketChange(ticket, publication);
        }
        catch (Exception exception)
        {
            LogFailure(ticket, "saving publication reference", exception);

            return new TicketChange(ticket, publication, ReferenceSaved: false);
        }
    }

    private async Task<TicketChange> RemoveCopiesAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var currentRemains = await RemoveCopyAsync(ticket, ticket.ChannelId, ticket.MessageId, cancellationToken);

        var previousRemains = await RemoveCopyAsync(
            ticket,
            ticket.PreviousChannelId,
            ticket.PreviousMessageId,
            cancellationToken);

        return new TicketChange(
            ticket,
            LinkedCopyRemains: currentRemains || previousRemains || ticket.PublicationPending);
    }

    private async Task<bool> RemoveCopyAsync(
        Ticket ticket,
        ulong? channelId,
        ulong? msgId,
        CancellationToken cancellationToken)
    {
        if (channelId is null || msgId is null)
            return false;

        try
        {
            return !await discord.RemoveAsync(
                ticket.GuildId,
                new TicketMessage(channelId.Value, msgId.Value),
                cancellationToken);
        }
        catch (Exception exception)
        {
            LogFailure(ticket, "removing channel copy", exception);

            return true;
        }
    }

    // Shared record access checks, independent of ticket type.
    private async Task CheckReadAsync(
        BotDbContext dbContext,
        TicketActor actor,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        var channelId = ticket.ChannelId;

        channelId ??= await dbContext.TicketConfigurations
            .Where(configuration => configuration.GuildId == actor.GuildId)
            .Select(configuration => configuration.DestinationChannelId)
            .SingleOrDefaultAsync(cancellationToken);

        if (channelId is not null)
            await discord.CheckChannelAsync(actor, channelId.Value, publishing: false, cancellationToken);
    }

    private static async Task<Ticket?> FindAsync(
        BotDbContext dbContext,
        ulong guildId,
        long number,
        bool tracking,
        CancellationToken cancellationToken)
    {
        if (number <= 0)
            throw new BotCommandException("Provide a positive ticket number.");

        IQueryable<Ticket> query =
            dbContext.Tickets.Where(ticket => ticket.GuildId == guildId && ticket.Number == number);

        if (!tracking)
            query = query.AsNoTracking();

        return await query
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static void RequireStaff(TicketActor actor)
    {
        var staff = actor.IsAdministrator
                    || actor.CanTimeoutMembers
                    || actor.CanBanMembers
                    || actor.CanKickMembers
                    || actor.CanMuteMembers
                    || actor.CanDeafenMembers
                    || actor.CanManageRoles
                    || actor.CanViewAuditLog;

        if (actor.GuildId == 0 || actor.UserId == 0 || !staff)
            throw new BotCommandException("Staff permission is required to access tickets in this server.");
    }

    private static void RequireHiddenAccess(TicketActor actor, bool hidden)
    {
        if (hidden && actor is { IsAdministrator: false, CanViewAuditLog: false })
            throw new BotCommandException("Accessing hidden tickets requires the View Audit Log permission.");
    }

    private void LogFailure(Ticket ticket, string operation, Exception exception)
    {
        // Exception details from Discord/database providers can contain moderation text.
        logger.LogError(
            "Ticket {Number} in guild {GuildId}: {Operation} failed ({FailureType})",
            ticket.Number,
            ticket.GuildId,
            operation,
            exception.GetType().Name
        );
    }

    public void Dispose() => _publicationLock.Dispose();
}