namespace SylvanBot.Features.Tickets;

using System.Threading.Channels;
using System.Diagnostics;
using SylvanBot.Gateway;

public sealed class TicketReactionGatewayHandler(DiscordSocketClient client, IServiceScopeFactory scopes, TicketDeliveryWorker worker, ILogger logger)
    : BackgroundService, IDiscordGatewayHandler
{
    private readonly Channel<ApprovalReaction> _reactions = Channel.CreateBounded<ApprovalReaction>(
        new BoundedChannelOptions(256) { SingleWriter = false, SingleReader = false, FullMode = BoundedChannelFullMode.Wait });

    public Task InitializeAsync(CancellationToken ct)
    {
        client.ReactionAdded += AddedAsync;
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.ReactionAdded -= AddedAsync;
        _reactions.Writer.TryComplete();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Parallel.ForEachAsync(_reactions.Reader.ReadAllAsync(stoppingToken),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stoppingToken },
                async (reaction, token) =>
                {
                    using var timing = new OperationTiming(logger
                        .ForContext("GuildId", reaction.GuildId)
                        .ForContext("MessageId", reaction.MessageId)
                        .ForContext("UserId", reaction.UserId)
                        .ForContext("queue_wait_ms", Stopwatch.GetElapsedTime(reaction.QueuedAt).TotalMilliseconds), "ticket.reaction_approval", "execute");

                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var coordinator = scope.ServiceProvider.GetRequiredService<TicketCoordinator>();
                        var approved = await coordinator.ApproveReactionAsync(reaction.GuildId, reaction.ChannelId, reaction.MessageId, reaction.UserId, token);

                        if (approved)
                            worker.Wake(reaction.GuildId);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (BotCommandException exception)
                    {
                        logger.Information("Ticket reaction approval refused on message {MessageId} for {UserId}: {Reason}",
                            reaction.MessageId, reaction.UserId, exception.Message);
                    }
                    catch (Exception exception)
                    {
                        logger.Error(exception, "Ticket reaction approval failed on message {MessageId} for {UserId}", reaction.MessageId, reaction.UserId);
                    }
                });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown cancels queued transient reactions.
        }
    }

    internal static bool IsApprovalEmoji(string name, bool custom)
    {
        if (custom)
            return string.Equals(name, "ratified", StringComparison.Ordinal);

        return name.Replace("\uFE0F", string.Empty, StringComparison.Ordinal) is "✅" or "👍";
    }

    private Task AddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        var bot = reaction.UserId == client.CurrentUser.Id || (reaction.User.IsSpecified && reaction.User.Value.IsBot);

        if (bot || reaction.Channel is not IGuildChannel guildChannel || !IsApprovalEmoji(reaction.Emote.Name, reaction.Emote is Emote))
            return Task.CompletedTask;

        var work = new ApprovalReaction(guildChannel.GuildId, channel.Id, message.Id, reaction.UserId, Stopwatch.GetTimestamp());

        if (!_reactions.Writer.TryWrite(work))
            logger.Warning("Ticket reaction queue full or closed on message {MessageId} for {UserId}; toggle the reaction to retry", message.Id, reaction.UserId);

        return Task.CompletedTask;
    }

    private sealed record ApprovalReaction(ulong GuildId, ulong ChannelId, ulong MessageId, ulong UserId, long QueuedAt);
}
