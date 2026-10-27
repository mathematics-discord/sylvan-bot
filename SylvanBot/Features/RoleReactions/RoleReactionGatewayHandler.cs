using System.Threading.Channels;
using SylvanBot.Gateway;

namespace SylvanBot.Features.RoleReactions;

public sealed class RoleReactionGatewayHandler(
    DiscordSocketClient client,
    RoleReactionCoordinator coordinator,
    ILogger logger)
    : BackgroundService, IDiscordGatewayHandler
{
    // Producer-consumer: gateway callbacks enqueue; one owned worker performs Discord writes in order.
    private readonly Channel<ReactionWork> _reactions = Channel.CreateBounded<ReactionWork>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode     = BoundedChannelFullMode.Wait,
        });

    public Task InitializeAsync(CancellationToken ct)
    {
        client.ReactionAdded   += AddedAsync;
        client.ReactionRemoved += RemovedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.ReactionAdded   -= AddedAsync;
        client.ReactionRemoved -= RemovedAsync;
        _reactions.Writer.TryComplete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _reactions.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await coordinator.ProcessReactionAsync(
                        guildId: work.GuildId,
                        channelId: work.ChannelId,
                        messageId: work.MessageId,
                        userId: work.UserId,
                        emote: work.Emote,
                        added: work.Added,
                        ct: stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.Error(
                        exception,
                        "Role reaction failed in guild {GuildId} on message {MessageId} for member {UserId} and emoji {Emoji}",
                        work.GuildId,
                        work.MessageId,
                        work.UserId,
                        work.Emote.ToString());
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown cancels pending transient work.
        }
    }

    private Task AddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        return Enqueue(message.Id, channel.Id, reaction, added: true);
    }

    private Task RemovedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        return Enqueue(message.Id, channel.Id, reaction, added: false);
    }

    private Task Enqueue(ulong messageId, ulong channelId, SocketReaction reaction, bool added)
    {
        var botReaction = reaction.UserId == client.CurrentUser.Id
            || (reaction.User.IsSpecified && reaction.User.Value.IsBot);

        if (botReaction || reaction.Channel is not IGuildChannel guildChannel)
            return Task.CompletedTask;

        var work = new ReactionWork(guildChannel.GuildId, channelId, messageId, reaction.UserId, reaction.Emote, added);

        if (!_reactions.Writer.TryWrite(work))
        {
            logger.Warning(
                "Role reaction queue full or closed in guild {GuildId} on message {MessageId} for member {UserId}; toggle the reaction to retry",
                work.GuildId,
                messageId,
                reaction.UserId);
        }

        return Task.CompletedTask;
    }

    private sealed record ReactionWork(ulong GuildId, ulong ChannelId, ulong MessageId, ulong UserId, IEmote Emote, bool Added);
}