using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Features.Tickets;

public sealed class TicketWorkflow(
    IDbContextFactory<BotDbContext> contextFactory,
    ITicketDiscord discord,
    ILogger logger) : IDisposable
{
    public const int CommentLimit = 2000;

    // A single bot serialises saves through publication, including initial creation.
    // The database token also detects changes made outside this process.
    private readonly SemaphoreSlim _corrections = new(1, 1);

    public async Task<TicketConfiguration> ConfigureAsync(
        TicketActor actor, bool? enabled, ulong? channelId, CancellationToken cancellationToken = default)
    {
        if (!actor.IsAdministrator)
            throw new UnauthorizedAccessException();

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
            throw new InvalidOperationException("Choose a tickets channel before enabling notes.");

        if (destination is not null && (updatedEnabled || channelId is not null))
            await discord.CheckChannelAsync(actor, destination.Value, publishing: true, cancellationToken);

        configuration.Enabled = updatedEnabled;
        configuration.DestinationChannelId = destination;

        if (isNew)
            dbContext.TicketConfigurations.Add(configuration);

        await dbContext.SaveChangesAsync(cancellationToken);
        return configuration;
    }

    public async Task<ulong> PrepareCreateAsync(
        TicketActor actor, ulong subjectUserId, CancellationToken cancellationToken = default)
    {
        RequireNotePermission(actor);

        if (subjectUserId == 0)
            throw new ArgumentException("A nonzero subject ID is required.", nameof(subjectUserId));

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await dbContext.TicketConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.GuildId == actor.GuildId, cancellationToken);

        if (configuration is not { Enabled: true, DestinationChannelId: not null })
            throw new InvalidOperationException("Ticket notes are disabled or have no destination.");

        await discord.CheckChannelAsync(actor, configuration.DestinationChannelId.Value, publishing: true, cancellationToken);
        return configuration.DestinationChannelId.Value;
    }

    public async Task<TicketChange> CreateAsync(
        TicketActor actor, ulong subjectUserId, string comment, CancellationToken cancellationToken = default)
    {
        ValidateComment(comment);
        await _corrections.WaitAsync(cancellationToken);

        try
        {
            var channelId = await PrepareCreateAsync(actor, subjectUserId, cancellationToken);
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
            await dbContext.SaveChangesAsync(cancellationToken);
            return await PublishAsync(dbContext, ticket, channelId, cancellationToken);
        }
        finally
        {
            _corrections.Release();
        }
    }

    public async Task<Option<Ticket>> ShowAsync(
        TicketActor actor, long number, bool hidden = false, CancellationToken cancellationToken = default)
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
        TicketActor actor, string messageLink, bool hidden = false, CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        RequireHiddenAccess(actor, hidden);
        var msg = TicketReference.ParseMessageLink(messageLink, actor.GuildId);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.GuildId == actor.GuildId && ticket.Hidden == hidden)
            .SingleOrDefaultAsync(ticket =>
                (ticket.ChannelId == msg.ChannelId && ticket.MessageId == msg.MessageId)
                || (ticket.PreviousChannelId == msg.ChannelId && ticket.PreviousMessageId == msg.MessageId),
                cancellationToken);

        if (ticket is null)
            return new None();

        await CheckReadAsync(dbContext, actor, ticket, cancellationToken);
        return new Some<Ticket>(ticket);
    }

    public async Task<TicketPage> ListAsync(
        TicketActor actor, ulong subjectUserId, int page = 1, bool hidden = false,
        CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        RequireHiddenAccess(actor, hidden);

        if (subjectUserId == 0 || page is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(page));

        var canViewHidden = actor.IsAdministrator || actor.CanViewAuditLog;
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tickets = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.GuildId == actor.GuildId && ticket.SubjectUserId == subjectUserId
                && (!hidden || ticket.Hidden) && (canViewHidden || !ticket.Hidden))
            .OrderByDescending(ticket => ticket.CreatedAtUtc)
            .ThenByDescending(ticket => ticket.Id)
            .Skip((page - 1) * 10)
            .Take(11)
            .ToListAsync(cancellationToken);

        var visiblePage = tickets
            .Take(10)
            .ToImmutableArray();

        foreach (var ticket in visiblePage)
            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

        return new TicketPage(visiblePage, page, tickets.Count > 10);
    }

    public async Task<Ticket> PrepareEditAsync(
        TicketActor actor, long number, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: false, cancellationToken)
            ?? throw new KeyNotFoundException();

        RequireMutation(actor, ticket);
        await CheckReadAsync(dbContext, actor, ticket, cancellationToken);
        return ticket;
    }

    public async Task<Ticket> PrepareEditAsync(
        TicketActor actor, TicketMessage publication, CancellationToken cancellationToken = default)
    {
        RequireStaff(actor);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.GuildId == actor.GuildId)
            .SingleOrDefaultAsync(ticket =>
                (ticket.ChannelId == publication.ChannelId && ticket.MessageId == publication.MessageId)
                || (ticket.PreviousChannelId == publication.ChannelId && ticket.PreviousMessageId == publication.MessageId),
                cancellationToken)
            ?? throw new KeyNotFoundException();

        RequireMutation(actor, ticket);
        await discord.CheckChannelAsync(actor, publication.ChannelId, publishing: false, cancellationToken);
        await CheckReadAsync(dbContext, actor, ticket, cancellationToken);
        return ticket;
    }

    public Task<TicketChange> SetAsync(
        TicketActor actor, long number, string? duration = null, string? comment = null,
        bool? hidden = null, int? expectedVersion = null, CancellationToken cancellationToken = default)
        => CorrectAsync(actor, number, comment, duration, append: false, hidden, expectedVersion, cancellationToken);

    public Task<TicketChange> AppendAsync(
        TicketActor actor, long number, string comment, CancellationToken cancellationToken = default)
        => CorrectAsync(actor, number, comment, duration: null, append: true, hidden: null,
            expectedVersion: null, cancellationToken);

    public Task<TicketChange> HideAsync(
        TicketActor actor, long number, CancellationToken cancellationToken = default)
        => CorrectAsync(actor, number, comment: null, duration: null, append: false, hidden: true,
            expectedVersion: null, cancellationToken);

    private async Task<TicketChange> CorrectAsync(
        TicketActor actor, long number, string? comment, string? duration, bool append, bool? hidden,
        int? expectedVersion, CancellationToken cancellationToken)
    {
        await _corrections.WaitAsync(cancellationToken);

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var ticket = await FindAsync(dbContext, actor.GuildId, number, tracking: true, cancellationToken)
                ?? throw new KeyNotFoundException();

            RequireMutation(actor, ticket);
            await CheckReadAsync(dbContext, actor, ticket, cancellationToken);

            if (duration is not null)
                throw new ArgumentException("Notes do not accept a duration.", nameof(duration));

            if (expectedVersion is not null && ticket.Version != expectedVersion)
                throw new DbUpdateConcurrencyException();

            if (hidden is null && comment is null)
                return new TicketChange(ticket, NoChange: true);

            if (append && !string.IsNullOrEmpty(ticket.Comment))
                comment = "\n" + comment;

            if (comment is not null)
                ValidateComment(comment, existingComment: append ? ticket.Comment : null);

            var updatedHidden = hidden ?? ticket.Hidden;
            var unhiding = ticket.Hidden && !updatedHidden;
            ulong? channelId = null;

            if (!updatedHidden)
            {
                channelId = await CorrectionChannelAsync(dbContext, actor, ticket, cancellationToken);
            }

            if (comment is not null)
                ticket.Comment = append ? ticket.Comment + comment : comment;

            ticket.Hidden = updatedHidden;
            ticket.Version++;
            ticket.LastModifiedByUserId = actor.UserId;
            ticket.LastModifiedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (hidden == true)
                return await RemoveCopiesAsync(ticket, cancellationToken);

            if (ticket.Hidden)
                return new TicketChange(ticket);

            return await PublishCorrectionAsync(dbContext, ticket, channelId!.Value, unhiding, cancellationToken);
        }
        finally
        {
            _corrections.Release();
        }
    }

    private async Task<ulong> CorrectionChannelAsync(
        BotDbContext dbContext, TicketActor actor, Ticket ticket, CancellationToken cancellationToken)
    {
        var channelId = ticket.ChannelId;
        channelId ??= await dbContext.TicketConfigurations
            .Where(configuration => configuration.GuildId == actor.GuildId)
            .Select(configuration => configuration.DestinationChannelId)
            .SingleOrDefaultAsync(cancellationToken);

        if (channelId is null)
            throw new InvalidOperationException("Configure a channel before publishing a correction.");

        await discord.CheckChannelAsync(actor, channelId.Value, publishing: true, cancellationToken);
        return channelId.Value;
    }

    private async Task<TicketChange> PublishCorrectionAsync(
        BotDbContext dbContext, Ticket ticket, ulong channelId, bool unhiding, CancellationToken cancellationToken)
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
            catch (Exception exception) when (exception is not OperationCanceledException)
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
        // a later correction to blindly create another publication, even after a restart.
        ticket.PublicationPending = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PublishAsync(dbContext, ticket, channelId, cancellationToken);
    }

    private async Task<TicketChange> PublishAsync(
        BotDbContext dbContext, Ticket ticket, ulong channelId, CancellationToken cancellationToken)
    {
        TicketMessage publication;

        try
        {
            publication = await discord.PublishAsync(ticket, channelId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure(ticket, "saving publication reference", exception);
            return new TicketChange(ticket, publication, ReferenceSaved: false);
        }
    }

    private async Task<TicketChange> RemoveCopiesAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var currentRemains = await RemoveCopyAsync(ticket, ticket.ChannelId, ticket.MessageId, cancellationToken);
        var previousRemains = await RemoveCopyAsync(
            ticket, ticket.PreviousChannelId, ticket.PreviousMessageId, cancellationToken);

        return new TicketChange(ticket, LinkedCopyRemains: currentRemains || previousRemains || ticket.PublicationPending);
    }

    private async Task<bool> RemoveCopyAsync(
        Ticket ticket, ulong? channelId, ulong? msgId, CancellationToken cancellationToken)
    {
        if (channelId is null || msgId is null)
            return false;

        try
        {
            return !await discord.RemoveAsync(ticket.GuildId, new TicketMessage(channelId.Value, msgId.Value), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure(ticket, "removing channel copy", exception);
            return true;
        }
    }

    private async Task CheckReadAsync(
        BotDbContext dbContext, TicketActor actor, Ticket ticket, CancellationToken cancellationToken)
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
        BotDbContext dbContext, ulong guildId, long number, bool tracking, CancellationToken cancellationToken)
    {
        if (number <= 0)
            throw new ArgumentOutOfRangeException(nameof(number));

        var query = dbContext.Tickets
            .Where(ticket => ticket.GuildId == guildId && ticket.Number == number);

        if (!tracking)
        {
            query = query
                .AsNoTracking();
        }

        return await query
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static void RequireStaff(TicketActor actor)
    {
        var staff = actor.IsAdministrator || actor.CanTimeoutMembers || actor.CanBanMembers || actor.CanKickMembers
            || actor.CanMuteMembers || actor.CanDeafenMembers || actor.CanManageRoles || actor.CanViewAuditLog;

        if (actor.GuildId == 0 || actor.UserId == 0 || !staff)
            throw new UnauthorizedAccessException();
    }

    private static void RequireNotePermission(TicketActor actor)
    {
        var canCreateNote = actor.GuildId != 0 && actor.UserId != 0
            && (actor.IsAdministrator || actor.CanTimeoutMembers);

        if (!canCreateNote)
            throw new UnauthorizedAccessException();
    }

    private static void RequireMutation(TicketActor actor, Ticket ticket)
    {
        RequireStaff(actor);
        RequireHiddenAccess(actor, ticket.Hidden);

        if (ticket.Action != TicketAction.Note)
            throw new NotSupportedException("This action type is read-only.");

        RequireNotePermission(actor);
    }

    private static void RequireHiddenAccess(TicketActor actor, bool hidden)
    {
        if (hidden && !actor.IsAdministrator && !actor.CanViewAuditLog)
            throw new UnauthorizedAccessException();
    }

    private static void ValidateComment(string comment, string? existingComment = null)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("A comment must contain non-whitespace content.", nameof(comment));

        if (comment.Length > CommentLimit - (existingComment?.Length ?? 0))
            throw new ArgumentException("Comments cannot exceed 2,000 characters.", nameof(comment));
    }

    private void LogFailure(Ticket ticket, string operation, Exception exception)
    {
        // Exception details from Discord/database providers can contain moderation text.
        logger.Error("Ticket {Number} in guild {GuildId}: {Operation} failed ({FailureType})",
            ticket.Number, ticket.GuildId, operation, exception.GetType().Name);
    }

    public void Dispose() => _corrections.Dispose();
}