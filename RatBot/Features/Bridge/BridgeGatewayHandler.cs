// ReSharper disable ClassNeverInstantiated.Global

namespace RatBot.Features.Bridge;

using System.Globalization;
using RatBot.Gateway;

public sealed class BridgeGatewayHandler : IDiscordGatewayHandler
{
    private readonly DiscordSocketClient _discordClient;
    private readonly BridgeChannels _channels;
    private readonly BridgeEndpoint _endpoint;

    public BridgeGatewayHandler(DiscordSocketClient discordClient, BridgeChannels channels, BridgeEndpoint endpoint)
    {
        _discordClient = discordClient;
        _channels = channels;
        _endpoint = endpoint;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await _channels.LoadAsync(ct);
        _discordClient.MessageReceived += OnMessageReceivedAsync;
    }

    public void Unsubscribe() => _discordClient.MessageReceived -= OnMessageReceivedAsync;

    private static string Id(ulong id) => id.ToString(CultureInfo.InvariantCulture);

    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        var connection = _endpoint.Connection;

        if (connection is null || message.Channel is not SocketGuildChannel guildChannel)
            return Task.CompletedTask;

        var eligible = BridgeEligibility.IsEligible(
            isGuildMessage: true,
            isHuman: message.Source == MessageSource.User && !message.Author.IsBot && !message.Author.IsWebhook,
            isConfigured: _channels.Contains(guildChannel.Guild.Id, message.Channel.Id),
            botUserId: _discordClient.CurrentUser?.Id ?? 0,
            content: message.Content,
            mentionedUserIds: ((IMessage)message).MentionedUserIds);

        if (!eligible)
            return Task.CompletedTask;

        var reference = message.Reference;

        var replyReference = reference is null
            ? null
            : new BridgeReference(
                reference.MessageId.IsSpecified ? Id(reference.MessageId.Value) : null,
                Id(reference.ChannelId),
                reference.GuildId.IsSpecified ? Id(reference.GuildId.Value) : null);

        ImmutableArray<BridgeAttachment> attachments = message.Attachments
            .Select(attachment => new BridgeAttachment(
                Id(attachment.Id),
                attachment.Filename,
                attachment.Url,
                attachment.ProxyUrl,
                attachment.Size,
                attachment.ContentType,
                attachment.Width,
                attachment.Height,
                attachment.Description,
                attachment.Ephemeral))
            .ToImmutableArray();

        var displayName = (message.Author as SocketGuildUser)?.DisplayName ??
                          message.Author.GlobalName ?? message.Author.Username;

        var forwarded = new BridgeMessage(
            Id(message.Id),
            Id(guildChannel.Guild.Id),
            Id(message.Channel.Id),
            message.Timestamp,
            Id(message.Author.Id),
            displayName,
            message.Content,
            replyReference,
            attachments);

        connection.TryForward(forwarded, new BridgeOrigin(message.Id, guildChannel.Guild.Id, message.Channel.Id));

        return Task.CompletedTask;
    }
}