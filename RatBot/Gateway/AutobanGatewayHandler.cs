using RatBot.Application.Moderation;
using RatBot.Domain.Moderation;

namespace RatBot.Gateway;

public sealed class AutobanGatewayHandler(DiscordSocketClient discordClient, IServiceScopeFactory scopeFactory, ILogger logger)
    : IDiscordGatewayHandler
{
    private readonly ILogger _logger = logger.ForContext<AutobanGatewayHandler>();

    public Task InitializeAsync(CancellationToken ct)
    {
        Subscribe();
        return Task.CompletedTask;
    }

    public void Unsubscribe() => discordClient.UserJoined -= HandleUserJoinedAsync;

    private async Task HandleUserJoinedAsync(SocketGuildUser user)
    {
        ulong guildId = user.Guild.Id;
        ulong userId = user.Id;

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IModerationService moderationService = scope.ServiceProvider.GetRequiredService<IModerationService>();

            AutobannedUser? autobannedUser = await moderationService.GetAutobanAsync(guildId, userId);

            if (autobannedUser is null)
                return;

            string reason = $"Autoban registered by moderator {autobannedUser.Moderator} at {autobannedUser.RegisteredAtUtc:O}.";

            await user.Guild.AddBanAsync(user.Id, 0, reason);

            _logger.Information("Banned autobanned user {UserId} immediately after joining guild {GuildId}", userId, guildId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed processing autoban join check for user {UserId} in guild {GuildId}", userId, guildId);
        }
    }

    private void Subscribe() => discordClient.UserJoined += HandleUserJoinedAsync;
}
