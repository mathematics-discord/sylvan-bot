using System.Net;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RoleReactions;
using SylvanBot.Application.Infrastructure.Data;
using static SylvanBot.Features.RoleReactions.RoleReactionInput;
using static SylvanBot.Features.RoleReactions.RoleReactionPolicy;

namespace SylvanBot.Features.RoleReactions;

public sealed class RoleReactionConfiguration(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contextFactory,
    RoleReactionRuntime runtime,
    ILogger logger
)
{
    public async Task<string> AddAsync(
        SocketGuild guild,
        SocketGuildUser moderator,
        string messageLink,
        ulong roleId,
        string emoji,
        CancellationToken ct)
    {
        DotNext.Optional<MessageLink> parsedLink = ParseMessageLink(text: messageLink, guildId: guild.Id);

        if (parsedLink is not { HasValue: true, Value: var link })
            return "Provide a Discord message link from this server, not a message ID.";

        DotNext.Optional<IEmote> parsedEmoji = ParseEmoji(emoji);

        if (parsedEmoji is not { HasValue: true, Value: var emote })
            return "Provide one Unicode emoji or a custom emoji mention.";

        var gate = runtime.ForGuild(guild.Id);
        await gate.WaitAsync(ct);

        try
        {
            var role = guild.GetRole(roleId);

            if (role is null)
                return "The target must be an existing, ordinary role other than @everyone.";

            DotNext.Optional<string> roleError = ValidateRole(guild: guild, role: role);

            if (roleError is { HasValue: true, Value: var reason })
                return reason;

            if (moderator.Id != guild.OwnerId && moderator.Hierarchy <= role.Position)
                return "The target role must be below your highest role.";

            var request = new RequestOptions { CancelToken = ct };
            var channel = await ReadChannelAsync(guildId: guild.Id, channelId: link.ChannelId, request: request);

            if (channel is not IGuildChannel guildChannel)
                return "The linked channel is unavailable or does not belong to this server.";

            DotNext.Optional<string> channelError = ValidateChannelAccess(guild: guild, moderator: moderator, channel: guildChannel);

            if (channelError is { HasValue: true, Value: var channelReason })
                return channelReason;

            var message = await channel.GetMessageAsync(id: link.MessageId, options: request);

            if (message is null)
                return "The linked message could not be found.";

            return await ConfigureReactionAsync(guildId: guild.Id, message: message, roleId: roleId, emote: emote, request: request, ct: ct);
        }
        catch (HttpException exception)
        {
            logger.Warning(
                exception: exception,
                "Could not read role reaction target in guild {GuildId} on message {MessageId}",
                propertyValue0: guild.Id,
                propertyValue1: link.MessageId);

            return "The bot could not read the linked message. Check that it still exists and the bot can access its channel or thread.";
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> RemoveAsync(SocketGuild guild, string messageLink, string emoji, CancellationToken ct)
    {
        DotNext.Optional<MessageLink> parsedLink = ParseMessageLink(text: messageLink, guildId: guild.Id);

        if (parsedLink is not { HasValue: true, Value: var link })
            return "Provide a Discord message link from this server, not a message ID.";

        DotNext.Optional<IEmote> parsedEmoji = ParseEmoji(emoji);

        if (parsedEmoji is not { HasValue: true, Value: var emote })
            return "Provide one Unicode emoji or a custom emoji mention.";

        var gate = runtime.ForGuild(guild.Id);
        await gate.WaitAsync(ct);

        try
        {
            var emojiKey = GetEmojiKey(emote);
            await using var db = await contextFactory.CreateDbContextAsync(ct);

            var mapping = await db.RoleReactions
               .SingleOrDefaultAsync(
                    mapping =>
                        mapping.GuildId == guild.Id
                        && mapping.ChannelId == link.ChannelId
                        && mapping.MessageId == link.MessageId
                        && mapping.EmojiKey == emojiKey,
                    cancellationToken: ct);

            if (mapping is null)
                return "No role reaction is configured for that message and emoji.";

            db.RoleReactions.Remove(mapping);
            await db.SaveChangesAsync(ct);

            return await ClearReactionsAsync(mapping: mapping, emote: emote, ct: ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string> ClearReactionsAsync(RoleReaction mapping, IEmote emote, CancellationToken ct)
    {
        try
        {
            var request = new RequestOptions { CancelToken = ct };
            var channel = await ReadChannelAsync(guildId: mapping.GuildId, channelId: mapping.ChannelId, request: request);
            var message = channel is null ? null : await channel.GetMessageAsync(id: mapping.MessageId, options: request);
            var configuredEmote = ParseEmoji(mapping.EmojiText) is { HasValue: true, Value: var savedEmote } ? savedEmote : emote;

            if (message is not null)
                await message.RemoveAllReactionsForEmoteAsync(emote: configuredEmote, options: request);
        }
        catch (HttpException exception) when (exception.HttpCode == HttpStatusCode.NotFound)
        {
            // A deleted message/channel needs no visible cleanup; the mapping is already disabled.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning(
                exception: exception,
                "Role reaction disabled but cleanup failed in guild {GuildId} on message {MessageId}",
                propertyValue0: mapping.GuildId,
                propertyValue1: mapping.MessageId);

            return
                "The mapping is removed and existing roles are unchanged, but the bot could not clear the emoji reactions. Clear them manually; the bot needs Manage Messages for cleanup.";
        }

        return "Removed the role reaction. Existing role memberships are unchanged.";
    }

    private async Task<IMessageChannel?> ReadChannelAsync(ulong guildId, ulong channelId, RequestOptions request)
    {
        IChannel? channel = client.GetChannel(channelId);
        channel ??= await client.Rest.GetChannelAsync(id: channelId, options: request);

        return channel is IGuildChannel guildChannel && guildChannel.GuildId == guildId ? channel as IMessageChannel : null;
    }

    private async Task<string> ConfigureReactionAsync(
        ulong guildId,
        IMessage message,
        ulong roleId,
        IEmote emote,
        RequestOptions request,
        CancellationToken ct)
    {
        var emojiKey = GetEmojiKey(emote);
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var mapping = await db.RoleReactions
           .SingleOrDefaultAsync(
                mapping => mapping.GuildId == guildId && mapping.MessageId == message.Id && mapping.EmojiKey == emojiKey,
                cancellationToken: ct);
        var conflicts = mapping is not null && (mapping.RoleId != roleId || mapping.ChannelId != message.Channel.Id);

        if (conflicts)
            return "That message and emoji already grant a different role. Remove the mapping first.";

        if (mapping is null)
        {
            mapping = new RoleReaction
            {
                GuildId = guildId,
                ChannelId = message.Channel.Id,
                MessageId = message.Id,
                EmojiKey = emojiKey,
                EmojiText = emote is Emote custom ? custom.ToString() : emote.Name,
                RoleId = roleId,
            };

            db.RoleReactions.Add(mapping);
            await db.SaveChangesAsync(ct);
        }

        try
        {
            await message.AddReactionAsync(emote: emote, options: request);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning(
                exception: exception,
                "Could not seed role reaction in guild {GuildId} on message {MessageId}",
                propertyValue0: guildId,
                propertyValue1: message.Id);

            return
                "The mapping is configured, but the bot could not add the reaction. Check emoji availability and bot permissions, then repeat this command.";
        }

        return $"Configured {emote} on the linked message to grant <@&{roleId}>. Removing an individual reaction removes the role.";
    }
}