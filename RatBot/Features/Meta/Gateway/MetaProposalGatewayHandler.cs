using RatBot.Features.Meta.BackgroundWorkers;
using RatBot.Gateway;
using RatBot.Infrastructure.Features.Meta;

namespace RatBot.Features.Meta.Gateway;

public sealed class MetaProposalGatewayHandler(
    DiscordSocketClient discordClient,
    IServiceScopeFactory scopeFactory,
    MetaProposalPollResolver pollResolver,
    ILogger logger
) : IDiscordGatewayHandler
{
    private readonly ILogger _logger = logger.ForContext<MetaProposalGatewayHandler>();

    public Task InitializeAsync(CancellationToken ct)
    {
        Subscribe();
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.ThreadCreated -= HandleThreadCreatedAsync;
        discordClient.ThreadDeleted -= HandleThreadDeletedAsync;
        discordClient.MessageDeleted -= HandleMessageDeletedAsync;
        discordClient.MessageUpdated -= HandleMessageUpdatedAsync;
    }

    private async Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (!channel.HasValue || !TryGetGuildId(channel.Value, out ulong guildId))
            return;

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            MetaProposalService service = scope.ServiceProvider.GetRequiredService<MetaProposalService>();
            ErrorOr<MetaProposalState> clearResult = await service.ClearDeletedPollByMessageAsync(guildId, message.Id);

            if (!clearResult.IsError)
                _logger.Information(
                    "Cleared deleted meta proposal poll message {PollMessageId} for state {StateId} in guild {GuildId}",
                    message.Id,
                    clearResult.Value.Id,
                    guildId
                );
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to handle meta proposal poll deletion event in guild {GuildId}", guildId);
        }
    }

    private async Task HandleMessageUpdatedAsync(
        Cacheable<IMessage, ulong> cachedMessage,
        SocketMessage updatedMessage,
        ISocketMessageChannel channel
    )
    {
        _ = cachedMessage;

        if (!TryGetGuildId(channel, out ulong guildId))
            return;

        if (updatedMessage is not IUserMessage pollMessage)
            return;

        Poll? pollValue = pollMessage.Poll;

        if (pollValue is not { } poll || poll.Results is not { IsFinalized: true })
            return;

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            MetaProposalService service = scope.ServiceProvider.GetRequiredService<MetaProposalService>();
            ErrorOr<MetaProposalState> stateResult = await service.GetByPollMessageAsync(guildId, pollMessage.Id);

            if (stateResult.IsError || stateResult.Value.Status is not MetaProposalStatus.PollActive)
                return;

            await pollResolver.ResolveFinalizedPollAsync(service, stateResult.Value, pollMessage, CancellationToken.None);

            _logger.Information(
                "Resolved manually finalized meta proposal poll {PollMessageId} for state {StateId} in guild {GuildId}",
                pollMessage.Id,
                stateResult.Value.Id,
                guildId
            );
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to handle manually finalized meta proposal poll update in guild {GuildId}", guildId);
        }
    }

    private async Task HandleThreadCreatedAsync(SocketThreadChannel thread)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            MetaProposalService service = scope.ServiceProvider.GetRequiredService<MetaProposalService>();
            ErrorOr<MetaSuggestionSettings> settingsResult = await service.GetSettingsAsync(thread.Guild.Id);

            if (settingsResult.IsError)
                return;

            MetaSuggestionSettings settings = settingsResult.Value;

            if (thread.ParentChannel.Id != settings.SuggestionsForumChannelId)
                return;

            ulong ownerId = ((IThreadChannel)thread).OwnerId;

            if (ownerId == 0)
            {
                _logger.Warning("Ignoring meta suggestion thread {ThreadId} because owner id was unavailable", thread.Id);

                return;
            }

            await service.TrackSuggestionThreadAsync(thread.Guild.Id, thread.Id, settings.SuggestionsForumChannelId, ownerId, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to handle meta suggestion thread creation event");
        }
    }

    private async Task HandleThreadDeletedAsync(Cacheable<SocketThreadChannel, ulong> thread)
    {
        if (!thread.HasValue)
            return;

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            MetaProposalService service = scope.ServiceProvider.GetRequiredService<MetaProposalService>();
            await service.ForgetDeletedUnsubmittedSuggestionAsync(thread.Value.Guild.Id, thread.Id);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to handle meta suggestion thread deletion event");
        }
    }

    private static bool TryGetGuildId(IChannel channel, out ulong guildId)
    {
        if (channel is IGuildChannel guildChannel)
        {
            guildId = guildChannel.GuildId;
            return true;
        }

        guildId = 0;
        return false;
    }

    private void Subscribe()
    {
        discordClient.ThreadCreated += HandleThreadCreatedAsync;
        discordClient.ThreadDeleted += HandleThreadDeletedAsync;
        discordClient.MessageDeleted += HandleMessageDeletedAsync;
        discordClient.MessageUpdated += HandleMessageUpdatedAsync;
    }
}
