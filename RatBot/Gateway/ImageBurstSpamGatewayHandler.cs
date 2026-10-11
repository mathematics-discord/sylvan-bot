using RatBot.Application.Moderation;
using RatBot.Configuration;

namespace RatBot.Gateway;

public sealed class ImageBurstSpamGatewayHandler(
    DiscordSocketClient discordClient,
    ImageBurstSpamDetector detector,
    ImageBurstSpamDetectorSettings detectorSettings,
    IServiceScopeFactory scopeFactory,
    IOptions<DiscordOptions> options,
    ILogger logger
) : IDiscordGatewayHandler
{
    private readonly ILogger _logger = logger.ForContext<ImageBurstSpamGatewayHandler>();
    private readonly DiscordOptions _options = options.Value;

    private static bool HasStaffPermissions(GuildPermissions permissions)
    {
        return permissions.Administrator
               || permissions.ManageGuild
               || permissions.ManageMessages
               || permissions.BanMembers
               || permissions.KickMembers
               || permissions.ModerateMembers;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ImageSpamSettingsService settingsService = scope.ServiceProvider.GetRequiredService<ImageSpamSettingsService>();
        await settingsService.LoadEnabledSettingsAsync(ct).ConfigureAwait(false);

        Subscribe();
    }

    public void Unsubscribe()
    {
        discordClient.MessageReceived -= HandleMessageReceivedAsync;
    }

    private async Task HandleDetectionAsync(ImageBurstDetection detection)
    {
        SocketGuild guild = discordClient.GetGuild(detection.GuildId);

        if (!detectorSettings.TryGet(detection.GuildId, out ImageBurstSpamDetectorOptions currentSettings))
            return;

        string reason =
            "Automatic image-burst spam detection: "
            + $"{detection.ChannelIds.Count} channels with {currentSettings.RequiredAttachmentCount}+ attachments "
            + $"in {currentSettings.Window} seconds.";

        await guild.AddBanAsync(detection.UserId, _options.ImageBurstSpamHistoryPruneDays, reason)
            .ConfigureAwait(false);

        _logger.Warning("Banned user {UserId} for image-burst spam across {ChannelCount} channels", detection.UserId,
            detection.ChannelIds.Count);
    }

    private async Task HandleMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            ImageBurstMessage? qualifyingMessage = TryCreateQualifyingMessage(message);

            if (qualifyingMessage is null)
                return;

            ImageBurstDetection? detection = detector.Observe(qualifyingMessage);

            if (detection is null)
                return;

            await HandleDetectionAsync(detection).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed processing image-burst spam detection for message {MessageId}", message.Id);
        }
    }

    private bool IsExempt(SocketGuildUser user)
    {
        return _options.ImageBurstSpamAllowlistedUserIds.Contains(user.Id)
               || user.Roles.Any(role => _options.ImageBurstSpamAllowlistedRoleIds.Contains(role.Id))
               || HasStaffPermissions(user.GuildPermissions);
    }

    private void Subscribe()
    {
        discordClient.MessageReceived += HandleMessageReceivedAsync;
    }

    private ImageBurstMessage? TryCreateQualifyingMessage(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage)
            return null;

        if (userMessage.Source != MessageSource.User)
            return null;

        if (userMessage.Author.IsBot || userMessage.Author.IsWebhook)
            return null;

        if (userMessage.Channel is not SocketTextChannel channel)
            return null;

        if (userMessage.Author is not SocketGuildUser guildUser)
            return null;

        if (!detectorSettings.TryGet(channel.Guild.Id, out ImageBurstSpamDetectorOptions currentSettings))
            return null;

        if (userMessage.Attachments.Count < currentSettings.RequiredAttachmentCount)
            return null;

        if (IsExempt(guildUser))
            return null;

        ImageBurstAttachment[] attachments =
            userMessage.Attachments.Select(x => new ImageBurstAttachment(x.Url)).ToArray();

        return new ImageBurstMessage(channel.Guild.Id, guildUser.Id, channel.Id, userMessage.Timestamp, attachments);
    }
}