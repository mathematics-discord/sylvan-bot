using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RoleReactions;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Infrastructure.Data;
using static SylvanBot.Features.RoleReactions.RoleReactionInput;
using static SylvanBot.Features.RoleReactions.RoleReactionPolicy;

namespace SylvanBot.Features.RoleReactions;

using DotNext;

public sealed class RoleReactionCoordinator(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contextFactory,
    RoleReactionRuntime runtime,
    ILogger logger
)
{
    public async Task ProcessReactionAsync(
        ulong guildId,
        ulong channelId,
        ulong messageId,
        ulong userId,
        IEmote emote,
        bool added,
        CancellationToken ct)
    {
        var gate = runtime.ForGuild(guildId);
        await gate.WaitAsync(ct);

        try
        {
            var emojiKey = GetEmojiKey(emote);
            await using var db = await contextFactory.CreateDbContextAsync(ct);

            var mapping = await db.RoleReactions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    mapping =>
                        mapping.GuildId == guildId
                        && mapping.ChannelId == channelId
                        && mapping.MessageId == messageId
                        && mapping.EmojiKey == emojiKey,
                    ct);

            if (mapping is null)
                return;

            var guild = client.GetGuild(guildId);

            if (guild is null)
                return;

            var request = new RequestOptions { CancelToken = ct, AuditLogReason = $"Role reaction on message {messageId}" };

            var member = guild.GetUser(userId) ?? (IGuildUser?)await client.Rest.GetGuildUserAsync(guildId, userId, request);

            if (member?.IsBot != false)
                return;

            await UpdateRoleAsync(guild, member, mapping, added, request);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task UpdateRoleAsync(SocketGuild guild, IGuildUser member, RoleReaction mapping, bool added, RequestOptions request)
    {
        var role = guild.GetRole(mapping.RoleId);

        Optional<string> roleError = role switch
        {
            null => new Optional<string>("The target must be an existing, ordinary role other than @everyone."),
            _ when added => ValidateRole(guild, role),
            _ => ValidateManageableRole(guild, role),
        };

        if (roleError is { HasValue: true, Value: var reason })
        {
            logger.Warning(
                "Skipped role reaction in guild {GuildId} on message {MessageId} for member {UserId}, role {RoleId}: {Reason}",
                guild.Id,
                mapping.MessageId,
                member.Id,
                mapping.RoleId,
                reason);

            return;
        }

        try
        {
            if (added)
                await member.AddRoleAsync(mapping.RoleId, request);
            else
                await member.RemoveRoleAsync(mapping.RoleId, request);
        }
        catch (HttpException exception)
        {
            logger.Warning(
                exception,
                "Could not update role {RoleId} for member {UserId} in guild {GuildId} from reaction on message {MessageId}",
                mapping.RoleId,
                member.Id,
                guild.Id,
                mapping.MessageId);
        }
    }
}