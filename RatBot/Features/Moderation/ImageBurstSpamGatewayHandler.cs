using RatBot.Gateway;

namespace RatBot.Features.Moderation;

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
            Option<ImageBurstMessage> qualifyingResult = TryCreateQualifyingMessage(message);

            if (qualifyingResult.Value is not Some<ImageBurstMessage>(var qualifyingMessage))
                return;

            Option<ImageBurstDetection> detectionResult = detector.Observe(qualifyingMessage);

            if (detectionResult.Value is not Some<ImageBurstDetection>(var detection))
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

    private Option<ImageBurstMessage> TryCreateQualifyingMessage(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage)
            return new None();

        if (userMessage.Source != MessageSource.User)
            return new None();

        if (userMessage.Author.IsBot || userMessage.Author.IsWebhook)
            return new None();

        if (userMessage.Channel is not SocketTextChannel channel)
            return new None();

        if (userMessage.Author is not SocketGuildUser guildUser)
            return new None();

        if (!detectorSettings.TryGet(channel.Guild.Id, out ImageBurstSpamDetectorOptions currentSettings))
            return new None();

        if (userMessage.Attachments.Count < currentSettings.RequiredAttachmentCount)
            return new None();

        if (IsExempt(guildUser))
            return new None();

        ImageBurstAttachment[] attachments =
            userMessage.Attachments.Select(x => new ImageBurstAttachment(x.Url)).ToArray();

        return new Some<ImageBurstMessage>(
            new ImageBurstMessage(channel.Guild.Id, guildUser.Id, channel.Id, userMessage.Timestamp, attachments));
    }
}