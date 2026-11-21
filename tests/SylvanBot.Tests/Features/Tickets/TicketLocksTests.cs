using Serilog;
using Serilog.Core;
using SylvanBot.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketLocksTests
{
    [Test]
    public async Task TicketAsync_DifferentTickets_ProceedWhileSameTicketWaits()
    {
        // Arrange
        await using Logger logger = new LoggerConfiguration().CreateLogger();
        TicketLocks locks = new TicketLocks(logger);
        using IDisposable first = await locks.TicketAsync(id: 1, CancellationToken.None);

        // Act
        Task<IDisposable> waiting = locks.TicketAsync(id: 1, CancellationToken.None);
        using IDisposable unrelated = await locks.TicketAsync(id: 2, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        waiting.IsCompleted.ShouldBeFalse();
        first.Dispose();
        using IDisposable next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TicketAsync_CancelledWaiter_DoesNotUnlockCurrentOwner()
    {
        // Arrange
        await using Logger logger = new LoggerConfiguration().CreateLogger();
        TicketLocks locks = new TicketLocks(logger);
        using IDisposable first = await locks.TicketAsync(id: 1, CancellationToken.None);
        using CancellationTokenSource cancellation = new CancellationTokenSource();

        // Act
        Task<IDisposable> cancelled = locks.TicketAsync(id: 1, cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await cancelled);
        Task<IDisposable> waiting = locks.TicketAsync(id: 1, CancellationToken.None);
        waiting.IsCompleted.ShouldBeFalse();
        first.Dispose();
        using IDisposable next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TicketsAsync_ReversedAndRepeatedIds_SerialisesWithoutDeadlock()
    {
        // Arrange
        await using Logger logger = new LoggerConfiguration().CreateLogger();
        TicketLocks locks = new TicketLocks(logger);
        using IDisposable first = await locks.TicketsAsync([2, 1, 2], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        Task<IDisposable> waiting = locks.TicketsAsync([1, 2], CancellationToken.None);

        // Assert
        waiting.IsCompleted.ShouldBeFalse();
        first.Dispose();
        using IDisposable next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TicketsAsync_CancellationAfterPartialAcquisition_ReleasesEarlierTickets()
    {
        // Arrange
        await using Logger logger = new LoggerConfiguration().CreateLogger();
        TicketLocks locks = new TicketLocks(logger);
        using IDisposable second = await locks.TicketAsync(id: 2, CancellationToken.None);
        using CancellationTokenSource cancellation = new CancellationTokenSource();

        // Act
        Task<IDisposable> waiting = locks.TicketsAsync([1, 2], cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);
        using IDisposable first = await locks.TicketAsync(id: 1, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Task<IDisposable> stillWaiting = locks.TicketAsync(id: 2, CancellationToken.None);
        stillWaiting.IsCompleted.ShouldBeFalse();
        second.Dispose();
        using IDisposable next = await stillWaiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Dispose_RepeatedRelease_DoesNotUnlockNextOwner()
    {
        // Arrange
        await using Logger logger = new LoggerConfiguration().CreateLogger();
        TicketLocks locks = new TicketLocks(logger);
        using IDisposable first = await locks.TicketAsync(id: 1, CancellationToken.None);
        first.Dispose();
        using IDisposable second = await locks.TicketAsync(id: 1, CancellationToken.None);

        // Act
        first.Dispose();
        Task<IDisposable> waiting = locks.TicketAsync(id: 1, CancellationToken.None);

        // Assert
        waiting.IsCompleted.ShouldBeFalse();
        second.Dispose();
        using IDisposable next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }
}