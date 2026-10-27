using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using SylvanBot.Gateway;

namespace SylvanBot.Features.EmojiAnalytics;

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

    private readonly ILogger               _logger  = logger.ForContext<EmojiAnalyticsGatewayHandler>();
    private readonly EmojiAnalyticsOptions _options = options.Value;

    public Task InitializeAsync(CancellationToken ct)
    {
        discordClient.MessageReceived          += HandleMessageReceivedAsync;
        discordClient.ReactionAdded            += HandleReactionAddedAsync;
        discordClient.ReactionRemoved          += HandleReactionRemovedAsync;
        discordClient.ReactionsCleared         += HandleReactionsClearedAsync;
        discordClient.ReactionsRemovedForEmote += HandleReactionsRemovedForEmoteAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.MessageReceived          -= HandleMessageReceivedAsync;
        discordClient.ReactionAdded            -= HandleReactionAddedAsync;
        discordClient.ReactionRemoved          -= HandleReactionRemovedAsync;
        discordClient.ReactionsCleared         -= HandleReactionsClearedAsync;
        discordClient.ReactionsRemovedForEmote -= HandleReactionsRemovedForEmoteAsync;
    }

    private static EmojiUsageIncrement[] ExtractMessageIncrements(ulong guildId, string content)
    {
        return EmojiRegex.Matches(content)
           .Select(match =>
                       ulong.TryParse(s: match.Groups["id"].Value, style: NumberStyles.None, provider: CultureInfo.InvariantCulture, out var id)
                           ? (ulong?)id
                           : null)
           .Where(id => id.HasValue)
           .GroupBy(id => id!.Value)
           .Select(group => new EmojiUsageIncrement(GuildId: guildId, EmojiId: group.Key, group.Count(), ReactionCount: 0))
           .ToArray();
    }

    private async Task HandleMessageReceivedAsync(IMessage message)
    {
        if (message is not IUserMessage
            || message.Source != MessageSource.User
            || message.Channel is not IGuildChannel guildChannel
            || string.IsNullOrWhiteSpace(message.Content)
            || !_options.IsEnabled(guildChannel.GuildId))
            return;

        EmojiUsageIncrement[] increments;

        try
        {
            increments = ExtractMessageIncrements(guildId: guildChannel.GuildId, content: message.Content);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.Warning(exception: ex, "Emoji parsing timed out for a message in guild {GuildId}", propertyValue: guildChannel.GuildId);

            return;
        }

        foreach (var increment in increments)
            if (!buffer.Writer.TryWrite(increment))
                await buffer.Writer.WriteAsync(increment);

        if (increments.Length > 0)
        {
            _logger.Debug(
                "Queued {Count} message emoji increments for guild {GuildId}",
                propertyValue0: increments.Length,
                propertyValue1: guildChannel.GuildId);
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
            LogReactionEvent("added_ignored_no_guild", emote: reaction.Emote, guildId: null);

            return;
        }

        if (!_options.IsEnabled(guildChannel.GuildId))
        {
            LogReactionEvent("added_ignored_disabled", emote: reaction.Emote, guildId: guildChannel.GuildId);

            return;
        }

        LogReactionEvent("added", emote: reaction.Emote, guildId: guildChannel.GuildId);

        var item = new EmojiUsageIncrement(GuildId: guildChannel.GuildId, EmojiId: customEmote.Id, MessageCount: 0, ReactionCount: 1);

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

        LogReactionEvent("removed", emote: reaction.Emote, guildId: guildId);

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

        _logger.ForContext("ReactionEventType", "cleared_all").ForContext("GuildId", value: guildId)
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

        LogReactionEvent("cleared_emote", emote: emote, guildId: guildId);

        return Task.CompletedTask;
    }

    private void LogReactionEvent(string reactionEventType, IEmote emote, ulong? guildId)
    {
        ulong? emojiId = emote is Emote customEmote ? customEmote.Id : null;

        _logger
           .ForContext("ReactionEventType", value: reactionEventType)
           .ForContext("GuildId", value: guildId)
           .ForContext("EmojiName", value: emote.Name)
           .ForContext("EmojiId", value: emojiId)
           .ForContext("IsCustomEmoji", value: emojiId.HasValue)
           .Debug("Discord reaction event recorded");
    }
}