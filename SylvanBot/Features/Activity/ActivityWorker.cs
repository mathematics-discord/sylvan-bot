using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Gateway;

namespace SylvanBot.Features.Activity;

public sealed class ActivityWorker(DiscordSocketClient client, ActivityService activity, IDbContextFactory<BotDbContext> contexts, ILogger logger)
    : BackgroundService, IDiscordGatewayHandler
{
    private readonly Channel<SocketUserMessage> _messages = Channel.CreateBounded<SocketUserMessage>(1024);

    public Task InitializeAsync(CancellationToken ct)
    {
        client.MessageReceived += ReceivedAsync;
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.MessageReceived -= ReceivedAsync;
        _messages.Writer.TryComplete();
    }

    private Task ReceivedAsync(SocketMessage message)
    {
        var eligible = message is SocketUserMessage && message.Channel is SocketGuildChannel
            && message.Source == MessageSource.User && !message.Author.IsBot && !message.Author.IsWebhook;
        if (eligible && !_messages.Writer.TryWrite((SocketUserMessage)message))
            logger.Warning("Activity queue full; skipped message {MessageId}", message.Id);

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(ProcessAsync(stoppingToken), CleanupAsync(stoppingToken));
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(ct))
        {
            try
            {
                await activity.ObserveAsync(message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Activity processing failed for message {MessageId}", message.Id);
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(ct);
                var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
                await db.ActivityAwards
                    .Where(row => row.AwardedAt <= cutoff)
                    .ExecuteDeleteAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Activity award cleanup failed");
            }
        }
    }
}
