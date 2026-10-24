namespace SylvanBot.Tests.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SylvanBot.Features.Tickets;
using SylvanBot.Infrastructure.Data;
using SylvanBot.Tests.Integration;

internal sealed class TicketTestFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    internal static TicketActor Moderator => new(100, 300, IsAdministrator: false, CanTimeoutMembers: true);
    internal static TicketActor SeniorModerator => Moderator with { CanViewAuditLog = true };
    internal static TicketActor Administrator => new(100, 301, IsAdministrator: true, CanTimeoutMembers: false);

    internal TicketTestFixture(EntityState? failSaveState = null)
    {
        _services = new ServiceCollection()
            .AddDbContextFactory<BotDbContext>(options => options
                .UseNpgsql(PostgresDatabaseFixture.ConnectionString)
                .AddInterceptors(new FailingTicketSaveInterceptor(failSaveState)))
            .BuildServiceProvider();

        Factory = _services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        Workflow = new TicketWorkflow(Factory, Discord, Serilog.Core.Logger.None);
    }

    internal IDbContextFactory<BotDbContext> Factory { get; }
    internal FakeTicketDiscord Discord { get; } = new();
    internal TicketWorkflow Workflow { get; }

    internal async Task EnableAsync()
    {
        await using var dbContext = await Factory.CreateDbContextAsync();
        dbContext.TicketConfigurations.Add(
            new TicketConfiguration { GuildId = 100, Enabled = true, DestinationChannelId = 200 });

        await dbContext.SaveChangesAsync();
    }

    internal async Task<Ticket> StoreAsync(
        long number = 32702,
        TicketAction action = TicketAction.Note,
        bool hidden = false,
        ulong guildId = 100,
        TicketStatus? status = null,
        string? comment = "Stored comment",
        TicketMessage? publication = null,
        TicketMessage? previousPublication = null)
    {
        await using var dbContext = await Factory.CreateDbContextAsync();
        var ticket = new Ticket
        {
            GuildId = guildId, Number = number, Action = action, Hidden = hidden,
            SubjectUserId = 123, ModeratorUserId = null, Comment = comment,
            CreatedAtUtc = DateTimeOffset.UtcNow, Status = status,
            ChannelId = publication?.ChannelId, MessageId = publication?.MessageId,
            PreviousChannelId = previousPublication?.ChannelId, PreviousMessageId = previousPublication?.MessageId,
        };

        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync();
        return ticket;
    }

    public async ValueTask DisposeAsync()
    {
        Workflow.Dispose();
        await _services.DisposeAsync();
    }

    private sealed class FailingTicketSaveInterceptor(EntityState? failSaveState) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var shouldFail = eventData.Context!.ChangeTracker.Entries<Ticket>()
                .Any(entry => entry.State == failSaveState);

            if (shouldFail)
                throw new DbUpdateException("Simulated save failure");

            return ValueTask.FromResult(result);
        }
    }
}