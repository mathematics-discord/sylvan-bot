namespace SylvanBot.Features.EmojiAnalytics;

using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using SylvanBot.Gateway;

public sealed class EmojiAnalyticsGatewayHandler(
    DiscordSocketClient discordClient,
    Channel<EmojiUsageIncrement> buffer,
    IOptions<EmojiAnalyticsOptions> options,
    ILogger logger) : IDiscordGatewayHandler
{
    private static readonly Regex EmojiRegex = new(
        @"<a?:\w{2,32}:(?<id>\d{17,21})>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly ILogger _logger = logger.ForContext<EmojiAnalyticsGatewayHandler>();
    private readonly EmojiAnalyticsOptions _options = options.Value;

    public Task InitializeAsync(CancellationToken ct)
    {
        discordClient.MessageReceived += HandleMessageReceivedAsync;
        discordClient.ReactionAdded += HandleReactionAddedAsync;
        discordClient.ReactionRemoved += HandleReactionRemovedAsync;
        discordClient.ReactionsCleared += HandleReactionsClearedAsync;
        discordClient.ReactionsRemovedForEmote += HandleReactionsRemovedForEmoteAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.MessageReceived -= HandleMessageReceivedAsync;
        discordClient.ReactionAdded -= HandleReactionAddedAsync;
        discordClient.ReactionRemoved -= HandleReactionRemovedAsync;
        discordClient.ReactionsCleared -= HandleReactionsClearedAsync;
        discordClient.ReactionsRemovedForEmote -= HandleReactionsRemovedForEmoteAsync;
    }

    private static EmojiUsageIncrement[] ExtractMessageIncrements(ulong guildId, string content)
    {
        return EmojiRegex.Matches(content)
            .Select(match =>
                ulong.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? (ulong?)id
                    : null)
            .Where(id => id.HasValue)
            .GroupBy(id => id!.Value)
            .Select(group => new EmojiUsageIncrement(guildId, group.Key, group.Count(), 0))
            .ToArray();
    }

    private async Task HandleMessageReceivedAsync(IMessage message)
    {
        if (message is not IUserMessage || message.Source != MessageSource.User
                                        || message.Channel is not IGuildChannel guildChannel
                                        || string.IsNullOrWhiteSpace(message.Content) ||
                                        !_options.IsEnabled(guildChannel.GuildId))
            return;

        EmojiUsageIncrement[] increments;

        try
        {
            increments = ExtractMessageIncrements(guildChannel.GuildId, message.Content);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.Warning(ex, "Emoji parsing timed out for a message in guild {GuildId}", guildChannel.GuildId);

            return;
        }

        foreach (var increment in increments)
        {
            if (!buffer.Writer.TryWrite(increment))
                await buffer.Writer.WriteAsync(increment);
        }

        if (increments.Length > 0)
        {
            _logger.Debug(
                "Queued {Count} message emoji increments for guild {GuildId}",
                increments.Length,
                guildChannel.GuildId);
        }
    }

    private async Task HandleReactionAddedAsync(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction)
    {
        _ = message;

        if (reaction.Emote is not Emote customEmote)
            return;

        if (!channel.HasValue || channel.Value is not IGuildChannel guildChannel)
        {
            LogReactionEvent("added_ignored_no_guild", reaction.Emote, null);

            return;
        }

        if (!_options.IsEnabled(guildChannel.GuildId))
        {
            LogReactionEvent("added_ignored_disabled", reaction.Emote, guildChannel.GuildId);

            return;
        }

        LogReactionEvent("added", reaction.Emote, guildChannel.GuildId);

        var item = new EmojiUsageIncrement(guildChannel.GuildId, customEmote.Id, 0, 1);

        if (!buffer.Writer.TryWrite(item))
            await buffer.Writer.WriteAsync(item);
    }

    private Task HandleReactionRemovedAsync(
        Cacheable<IUserMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        SocketReaction reaction)
    {
        _ = cachedMessage;

        ulong? guildId = cachedChannel is { HasValue: true, Value: IGuildChannel guildChannel }
            ? guildChannel.GuildId
            : null;

        LogReactionEvent("removed", reaction.Emote, guildId);

        return Task.CompletedTask;
    }

    private Task HandleReactionsClearedAsync(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel)
    {
        _ = message;

        ulong? guildId = channel is { HasValue: true, Value: IGuildChannel guildChannel }
            ? guildChannel.GuildId
            : null;

        _logger.ForContext("ReactionEventType", "cleared_all").ForContext("GuildId", guildId)
            .Information("Discord reaction event recorded");

        return Task.CompletedTask;
    }

    private Task HandleReactionsRemovedForEmoteAsync(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        IEmote emote)
    {
        _ = message;

        ulong? guildId = channel is { HasValue: true, Value: IGuildChannel guildChannel }
            ? guildChannel.GuildId
            : null;

        LogReactionEvent("cleared_emote", emote, guildId);

        return Task.CompletedTask;
    }

    private void LogReactionEvent(string reactionEventType, IEmote emote, ulong? guildId)
    {
        ulong? emojiId = emote is Emote customEmote ? customEmote.Id : null;

        _logger
            .ForContext("ReactionEventType", reactionEventType)
            .ForContext("GuildId", guildId)
            .ForContext("EmojiName", emote.Name)
            .ForContext("EmojiId", emojiId)
            .ForContext("IsCustomEmoji", emojiId.HasValue)
            .Debug("Discord reaction event recorded");
    }
}