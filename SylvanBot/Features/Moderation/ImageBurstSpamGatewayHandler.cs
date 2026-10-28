using SylvanBot.Gateway;

namespace SylvanBot.Features.Moderation;

public sealed class ImageBurstSpamGatewayHandler(
    DiscordSocketClient discordClient,
    ImageBurstSpamDetector detector,
    ImageSpamSettingsService settings,
    IHostApplicationLifetime lifetime,
    IOptions<DiscordOptions> options,
    ILogger logger
) : IDiscordGatewayHandler
{
    private readonly ILogger _logger = logger.ForContext<ImageBurstSpamGatewayHandler>();
    private readonly DiscordOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken ct)
    {
        await settings.LoadEnabledSettingsAsync(ct);

        Subscribe();
    }

    public void Unsubscribe()
    {
        discordClient.MessageReceived -= HandleMessageReceivedAsync;
    }

    private static bool HasStaffPermissions(GuildPermissions permissions) =>
        permissions.Administrator
        || permissions.ManageGuild
        || permissions.ManageMessages
        || permissions.BanMembers
        || permissions.KickMembers
        || permissions.ModerateMembers;

    private async Task HandleDetectionAsync(ImageBurstDetection detection)
    {
        var guild = discordClient.GetGuild(detection.GuildId);

        if (settings.Current(detection.GuildId).Value is not Some<ImageBurstSpamDetectorOptions>(var currentSettings))
            return;

        var reason =
            "Automatic image-burst spam detection: "
            + $"{detection.ChannelIds.Count} channels with {currentSettings.RequiredAttachmentCount}+ attachments "
            + $"in {currentSettings.Window} seconds.";

        await guild.AddBanAsync(
            userId: detection.UserId,
            pruneDays: _options.ImageBurstSpamHistoryPruneDays,
            reason: reason,
            options: new RequestOptions { CancelToken = lifetime.ApplicationStopping });

        _logger.Warning(
            "Banned user {UserId} for image-burst spam across {ChannelCount} channels",
            propertyValue0: detection.UserId,
            propertyValue1: detection.ChannelIds.Count);
    }

    private async Task HandleMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            Option<ImageBurstMessage> qualifyingResult = TryCreateQualifyingMessage(message);

            if (qualifyingResult.Value is not Some<ImageBurstMessage>(var qualifyingMessage))
                return;

            Option<ImageBurstDetection> detectionResult = detector.Observe(qualifyingMessage, settings.Current(qualifyingMessage.GuildId));

            if (detectionResult.Value is not Some<ImageBurstDetection>(var detection))
                return;

            await HandleDetectionAsync(detection);
        }
        catch (Exception ex)
        {
            _logger.Error(exception: ex, "Failed processing image-burst spam detection for message {MessageId}", propertyValue: message.Id);
        }
    }

    private bool IsExempt(SocketGuildUser user)
    {
        return _options.ImageBurstSpamAllowlistedUserIds.Contains(user.Id)
               || user.Roles
                   .Any(role => _options.ImageBurstSpamAllowlistedRoleIds.Contains(role.Id))
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

        if (settings.Current(channel.Guild.Id).Value is not Some<ImageBurstSpamDetectorOptions>(var currentSettings))
            return new None();

        if (userMessage.Attachments.Count < currentSettings.RequiredAttachmentCount)
            return new None();

        if (IsExempt(guildUser))
            return new None();

        ImageBurstAttachment[] attachments = userMessage.Attachments
            .Select(x => new ImageBurstAttachment(x.Url))
            .ToArray();

        return new Some<ImageBurstMessage>(
            new ImageBurstMessage(
                GuildId: channel.Guild.Id,
                UserId: guildUser.Id,
                ChannelId: channel.Id,
                Timestamp: userMessage.Timestamp,
                Attachments: attachments));
    }
}