namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Gateway;

public sealed class TicketDeliveryWorker(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contextFactory,
    IServiceScopeFactory scopes,
    TicketReviewSummary reviewSummary,
    ILogger logger) : BackgroundService, IDiscordGatewayHandler
{
    private readonly SemaphoreSlim _publications = new SemaphoreSlim(0, 1);
    private readonly SemaphoreSlim _prompts = new SemaphoreSlim(0, 1);
    private readonly ILogger _logger = logger.ForContext<TicketDeliveryWorker>();
    private bool _ready;

    public Task InitializeAsync(CancellationToken ct)
    {
        client.Ready += OnReadyAsync;
        client.MessageReceived += OnMessageAsync;
        client.MessageDeleted += OnMessageDeletedAsync;
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.Ready -= OnReadyAsync;
        client.MessageReceived -= OnMessageAsync;
        client.MessageDeleted -= OnMessageDeletedAsync;
    }

    public void Wake(ulong guildId = 0)
    {
        if (guildId != 0)
            reviewSummary.MarkDirty(guildId);

        Signal(_publications);
        Signal(_prompts);
    }

    private static void Signal(SemaphoreSlim semaphore)
    {
        try
        {
            semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Durable work is already scheduled.
        }
    }

    public override async Task StopAsync(CancellationToken token)
    {
        Unsubscribe();
        await base.StopAsync(token);
    }

    public override void Dispose()
    {
        _publications.Dispose();
        _prompts.Dispose();
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken token) => Task.WhenAll(
        RunAsync(_publications, PublishAsync, token),
        RunAsync(_prompts, ProcessModeratorsAsync, token));

    private async Task RunAsync(SemaphoreSlim wake, Func<CancellationToken, Task> process, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await wake.WaitAsync(TimeSpan.FromSeconds(30), token);
                if (!_ready || client.ConnectionState != ConnectionState.Connected)
                    continue;

                try
                {
                    await process(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Warning("Ticket delivery pass failed ({FailureType})", exception.GetType().Name);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.Debug("Ticket delivery loop stopped");
        }
    }

    private Task OnReadyAsync()
    {
        _ready = true;
        Wake();
        return Task.CompletedTask;
    }

    private Task OnMessageAsync(SocketMessage message)
    {
        if (reviewSummary.MarkChannelDirty(message.Channel.Id))
            Signal(_publications);
        return Task.CompletedTask;
    }

    private Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (reviewSummary.MarkChannelDirty(channel.Id))
            Signal(_publications);
        return Task.CompletedTask;
    }

    private async Task PublishAsync(CancellationToken token)
    {
        long after = 0;
        while (true)
        {
            await using var db = await contextFactory.CreateDbContextAsync(token);
            var pending = await db.Tickets
                .AsNoTracking()
                .Where(ticket => ticket.Id > after
                    && db.TicketConfigurations.Any(config => config.GuildId == ticket.GuildId && config.Enabled && config.DestinationChannelId != null)
                    && ((!ticket.Hidden && !ticket.SendUncertain
                         && !ticket.Publications.Any(reference => reference.Current && !reference.Removed && reference.Revision == ticket.Version))
                        || ticket.Publications.Any(reference => !reference.Removed
                            && (reference.ApprovalReactionCleanupPending || ticket.Hidden || !reference.Current || reference.Revision != ticket.Version))))
                .OrderBy(ticket => ticket.Id)
                .Select(ticket => new { ticket.GuildId, ticket.Id })
                .Take(100)
                .ToListAsync(token);

            if (pending.Count == 0)
                break;

            foreach (var ticket in pending)
            {
                await using var scope = scopes.CreateAsyncScope();
                var publisher = scope.ServiceProvider.GetRequiredService<TicketPublisher>();
                var result = await publisher.SynchronizeAsync(ticket.GuildId, ticket.Id, token);

                if (result.Changed)
                    reviewSummary.MarkDirty(ticket.GuildId);

                if (result.Outcome == TicketPublicationOutcome.Pending)
                    Signal(_publications);
            }

            after = pending[^1].Id;
        }

        await reviewSummary.RefreshAsync(token);
    }

    private async Task ProcessModeratorsAsync(CancellationToken token)
    {
        ulong after = 0;

        while (true)
        {
            await using var db = await contextFactory.CreateDbContextAsync(token);

            IQueryable<ulong> enabled = db.TicketConfigurations
                .Where(config => config.Enabled && config.DestinationChannelId != null)
                .Select(config => config.GuildId);

            IQueryable<ulong> moderators = db.TicketModerators
                .Where(moderator => moderator.ActiveTicketId != null)
                .Select(moderator => moderator.UserId);

            List<ulong> users = await db.Tickets
                .Where(ticket => !ticket.Hidden
                                 && !ticket.ReminderExempt
                                 && (ticket.InitialPromptAfterUtc != null || string.IsNullOrWhiteSpace(ticket.Comment))
                                 && enabled.Contains(ticket.GuildId))
                .Select(ticket => ticket.ModeratorUserId)
                .Union(moderators)
                .Where(user => user > after)
                .OrderBy(user => user)
                .Take(100)
                .ToListAsync(token);

            if (users.Count == 0)
                return;

            foreach (var user in users)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var reminders = scope.ServiceProvider.GetRequiredService<TicketReminders>();
                    await reminders.RemindAsync(user, force: false, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Warning("Ticket reminder failed for moderator {ModeratorId} ({FailureType})", user, exception.GetType().Name);
                }
            }

            after = users[^1];
        }
    }
}
