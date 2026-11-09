namespace SylvanBot.Tests.Features.Tickets;

using Serilog;
using SylvanBot.Features.Tickets;

public sealed class TicketLocksTests
{
    [Test]
    public async Task TicketAsync_DifferentTickets_ProceedWhileSameTicketWaits()
    {
        // Arrange
        await using var logger = new LoggerConfiguration().CreateLogger();
        var locks = new TicketLocks(logger);
        using var first = await locks.TicketAsync(1, CancellationToken.None);

        // Act
        Task<IDisposable> waiting = locks.TicketAsync(1, CancellationToken.None);
        using var unrelated = await locks.TicketAsync(2, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        waiting.IsCompleted.ShouldBeFalse();
        first.Dispose();
        using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TicketAsync_CancelledWaiter_DoesNotUnlockCurrentOwner()
    {
        // Arrange
        await using var logger = new LoggerConfiguration().CreateLogger();
        var locks = new TicketLocks(logger);
        using var first = await locks.TicketAsync(1, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        // Act
        Task<IDisposable> cancelled = locks.TicketAsync(1, cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await cancelled);
        Task<IDisposable> waiting = locks.TicketAsync(1, CancellationToken.None);
        waiting.IsCompleted.ShouldBeFalse();
        first.Dispose();
        using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TicketsAsync_ReversedAndRepeatedIds_SerialisesWithoutDeadlock()
    {
        // Arrange
        await using var logger = new LoggerConfiguration().CreateLogger();
        var locks = new TicketLocks(logger);
        using var first = await locks.TicketsAsync([2, 1, 2], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        Task<IDisposable> waiting = locks.TicketsAsync([1, 2], CancellationToken.None);

        // Assert
        waiting.IsCompleted.ShouldBeFalse();
        first.Dispose();
        using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TicketsAsync_CancellationAfterPartialAcquisition_ReleasesEarlierTickets()
    {
        // Arrange
        await using var logger = new LoggerConfiguration().CreateLogger();
        var locks = new TicketLocks(logger);
        using var second = await locks.TicketAsync(2, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        // Act
        Task<IDisposable> waiting = locks.TicketsAsync([1, 2], cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);
        using var first = await locks.TicketAsync(1, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Task<IDisposable> stillWaiting = locks.TicketAsync(2, CancellationToken.None);
        stillWaiting.IsCompleted.ShouldBeFalse();
        second.Dispose();
        using var next = await stillWaiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Dispose_RepeatedRelease_DoesNotUnlockNextOwner()
    {
        // Arrange
        await using var logger = new LoggerConfiguration().CreateLogger();
        var locks = new TicketLocks(logger);
        using var first = await locks.TicketAsync(1, CancellationToken.None);
        first.Dispose();
        using var second = await locks.TicketAsync(1, CancellationToken.None);

        // Act
        first.Dispose();
        Task<IDisposable> waiting = locks.TicketAsync(1, CancellationToken.None);

        // Assert
        waiting.IsCompleted.ShouldBeFalse();
        second.Dispose();
        using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
