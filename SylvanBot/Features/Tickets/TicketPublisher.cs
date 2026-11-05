using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Tickets;

using System.Text;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketPublisher(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    TicketBoundary boundary,
    ILogger logger)
{
    // The coordinator holds the single mutation/publication gate for every entry here.
    internal async Task<string> EnsureAsync(ulong guildId, long id, CancellationToken token)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var config = await TicketCoordinator.EnabledAsync(db, guildId, token);

            var ticket = await db.Tickets
                .SingleAsync(value => value.GuildId == guildId && value.Id == id, token);

            List<TicketPublication> references = ticket.Publications
                .Where(reference => !reference.Removed)
                .ToList();

            var guild = await client.Rest.GetGuildAsync(guildId, TicketBoundary.Options(token));
            var incomplete = false;

            foreach (var reference in references)
            {
                try
                {
                    if (!ticket.Hidden && reference.Current && reference.Revision == ticket.Version)
                        continue;

                    var channel = await guild.GetTextChannelAsync(reference.ChannelId, options: TicketBoundary.Options(token));

                    var message = channel is null
                        ? throw new BotCommandException("The stored ticket channel is unavailable.")
                        : await channel.GetMessageAsync(reference.MessageId, options: TicketBoundary.Options(token));

                    if (message is null)
                    {
                        reference.Removed = true;
                        reference.Current = false;
                        await db.SaveChangesAsync(token);

                        continue;
                    }

                    if (message.Author.Id != client.CurrentUser.Id)
                        throw new BotCommandException("Sylvan can only change messages it authored.");

                    var incompatible = TicketPresentation.UsesModerationEmbed(ticket) && message.Flags?.HasFlag(MessageFlags.ComponentsV2) == true;

                    if (ticket.Hidden || !reference.Current || incompatible)
                    {
                        await channel.DeleteMessageAsync(reference.MessageId, TicketBoundary.Options(token));
                        reference.Removed = true;
                        reference.Current = false;
                    }
                    else
                    {
                        await UpdateAsync(channel, reference.MessageId, ticket, token);
                        reference.Revision = ticket.Version;
                    }

                    await db.SaveChangesAsync(token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    incomplete = true;

                    logger.Warning(
                        "Ticket {TicketId}: message {MessageId} could not be synchronised ({FailureType})",
                        id,
                        reference.MessageId,
                        exception.GetType().Name);
                }
            }

            if (incomplete)
                return "\nSome recorded channel copies could not be synchronised. Their changes will retry; the saved record is retained.";

            if (ticket.SendUncertain)
                return "\nA new publication remains unconfirmed. Check the tickets channel; it will not be reposted automatically.";

            if (ticket.Hidden)
                return "\nThe record is hidden; recorded channel copies were removed. Restrictions continue normally.";

            var current = ticket.Publications
                .FirstOrDefault(reference => reference.Current && !reference.Removed);

            if (current is not null)
                return string.Empty;

            var destination = await guild.GetTextChannelAsync(config.DestinationChannelId!.Value, options: TicketBoundary.Options(token))
                              ?? throw new BotCommandException("The tickets channel is unavailable.");

            ticket.SendUncertain = true;
            await db.SaveChangesAsync(token);
            IUserMessage sent;

            try
            {
                sent = await SendAsync(destination, ticket, token);
            }
            catch (HttpException exception) when (exception.HttpCode is BadRequest or Forbidden or NotFound)
            {
                ticket.SendUncertain = false;
                await db.SaveChangesAsync(token);

                throw;
            }

            ticket.Publications.Add(
                new TicketPublication
                {
                    ChannelId = destination.Id, MessageId = sent.Id, Current = true, Revision = ticket.Version,
                });

            ticket.SendUncertain = false;
            await db.SaveChangesAsync(token);

            return $"\n[Jump to ticket message]({TicketPresentation.Link(guildId, destination.Id, sent.Id)})";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warning("Publication failed for ticket {TicketId} in guild {GuildId} ({FailureType})", id, guildId, exception.GetType().Name);

            return
                "\nThe ticket was saved, but publication could not be confirmed. Check the ticket and channel before repeating anything. Known-message changes will retry.";
        }
    }

    private static async Task<IUserMessage> SendAsync(IMessageChannel channel, Ticket ticket, CancellationToken token)
    {
        if (TicketPresentation.UsesModerationEmbed(ticket))
            return await channel.SendMessageAsync(
                embed: TicketPresentation.Describe(ticket),
                options: TicketBoundary.Options(token),
                allowedMentions: AllowedMentions.None);

        if (ticket.Comment.Length <= TicketPresentation.InlineCommentLimit)
            return await channel.SendMessageAsync(
                options: TicketBoundary.Options(token),
                allowedMentions: AllowedMentions.None,
                components: TicketPresentation.ChannelMessage(ticket),
                flags: MessageFlags.ComponentsV2);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));

        return await channel.SendFileAsync(
            stream,
            $"{ticket.Number}-comment.txt",
            components: TicketPresentation.ChannelMessage(ticket),
            flags: MessageFlags.ComponentsV2,
            allowedMentions: AllowedMentions.None,
            options: TicketBoundary.Options(token));
    }

    private static async Task UpdateAsync(IMessageChannel channel, ulong messageId, Ticket ticket, CancellationToken token)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));
        var moderation = TicketPresentation.UsesModerationEmbed(ticket);

        await channel.ModifyMessageAsync(
            messageId,
            properties =>
            {
                properties.Content = string.Empty;
                properties.Embeds = moderation ? new[] { TicketPresentation.Describe(ticket) } : Array.Empty<Embed>();
                properties.Components = moderation ? new ComponentBuilder().Build() : TicketPresentation.ChannelMessage(ticket);

                if (!moderation)
                {
                    properties.Flags = MessageFlags.ComponentsV2;

                    properties.Attachments = ticket.Comment.Length > TicketPresentation.InlineCommentLimit
                        ? new[] { new FileAttachment(stream, $"{ticket.Number}-comment.txt") }
                        : Array.Empty<FileAttachment>();
                }

                properties.AllowedMentions = AllowedMentions.None;
            },
            TicketBoundary.Options(token));
    }

    public async Task<TicketMessage?> FindMessageAsync(TicketActor actor, Ticket ticket, CancellationToken token)
    {
        if (actor.GuildId != ticket.GuildId)
            throw new BotCommandException("This ticket belongs to another server.");

        var guild = await client.Rest.GetGuildAsync(actor.GuildId, TicketBoundary.Options(token));
        var member = await guild.GetUserAsync(actor.UserId, TicketBoundary.Options(token));

        IOrderedEnumerable<TicketPublication> references = ticket.Publications
            .Where(reference => !reference.Removed)
            .OrderByDescending(reference => reference.Current);

        foreach (var reference in references)
        {
            try
            {
                var channel = await guild.GetTextChannelAsync(reference.ChannelId, options: TicketBoundary.Options(token));

                if (channel is not null
                    && member?.GetPermissions(channel).ViewChannel == true
                    && await channel.GetMessageAsync(reference.MessageId, options: TicketBoundary.Options(token)) is not null)
                    return reference.Message();
            }
            catch (HttpException exception) when (exception.HttpCode is NotFound or Forbidden)
            {
            }
        }

        return null;
    }

    internal async Task RemindAsync(ulong userId, bool force, CancellationToken token, ulong? requestedGuild = null)
    {
        await using var db = await contexts.CreateDbContextAsync(token);

        IQueryable<ulong> enabled = db.TicketConfigurations
            .Where(config => config.Enabled && config.DestinationChannelId != null)
            .Select(config => config.GuildId);

        var ticket = await db.Tickets
            .Where(value => value.ModeratorUserId == userId
                            && !value.Hidden
                            && !value.ReminderExempt
                            && value.Pending == null
                            && (value.InitialPromptPending
                                || string.IsNullOrWhiteSpace(value.Comment)
                                || (value.Action == TicketAction.Timeout && value.DurationSeconds == null))
                            && enabled.Contains(value.GuildId))
            .OrderByDescending(value => value.CreatedAtUtc)
            .ThenByDescending(value => value.Id)
            .FirstOrDefaultAsync(token);

        var moderator = await db.TicketModerators
            .SingleOrDefaultAsync(value => value.UserId == userId, token);

        if (requestedGuild is { } guildId && ticket is not null && ticket.GuildId != guildId)
            throw new BotCommandException("Your newest outstanding ticket is in another server. Request redelivery there.");

        if (moderator is null)
        {
            if (ticket is null)
                return;

            moderator = new TicketModerator { UserId = userId };
            db.TicketModerators.Add(moderator);
        }

        if (moderator.PromptMessageId is not null && moderator.ChannelId is not null)
        {
            var old = moderator.ActiveTicketId == ticket?.Id
                ? ticket
                : await db.Tickets
                    .SingleOrDefaultAsync(value => value.Id == moderator.ActiveTicketId, token);

            if (old is not null && !await enabled.ContainsAsync(old.GuildId, token))
                old = null;

            await RefreshPromptAsync(moderator, old, token);
        }

        if (ticket is null)
        {
            moderator.ClearDelivery();
            await db.SaveChangesAsync(token);

            if (force)
                throw new BotCommandException("Your ticket queue is empty.");

            return;
        }

        if (!force && !moderator.IsDeliveryDue(ticket.Id, DateTimeOffset.UtcNow))
        {
            moderator.PromptVersion = ticket.Version;
            await db.SaveChangesAsync(token);

            return;
        }

        var config = await TicketCoordinator.EnabledAsync(db, ticket.GuildId, token);
        // Persist backoff before sending, including when the DM outcome is uncertain.
        moderator.ActiveTicketId = ticket.Id;
        moderator.NextDeliveryUtc = DateTimeOffset.UtcNow.AddSeconds(config.ReminderIntervalSeconds);
        moderator.LastAttemptedVersion = ticket.Version;
        moderator.Version++;
        ticket.InitialPromptPending = false;
        await db.SaveChangesAsync(token);
        var actor = await boundary.GetActorAsync(ticket.GuildId, userId, token);
        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(token));
        var member = await guild.GetUserAsync(userId, TicketBoundary.Options(token));
        var destination = await guild.GetTextChannelAsync(config.DestinationChannelId!.Value, options: TicketBoundary.Options(token));

        if (!ticket.CanCorrect(actor) || destination is null || !member.GetPermissions(destination).ViewChannel)
            throw new BotCommandException("You no longer have access to complete this ticket.");

        var user = await client.Rest.GetUserAsync(userId, TicketBoundary.Options(token));
        var channel = await user.CreateDMChannelAsync(TicketBoundary.Options(token));

        var prompt = await channel.SendMessageAsync(
            $"**{guild.Name}** · Use **Edit** to review or adjust this ticket.",
            embed: TicketPresentation.Describe(ticket),
            components: TicketDmModule.Buttons(ticket),
            options: TicketBoundary.Options(token),
            allowedMentions: AllowedMentions.None);

        moderator.ChannelId = channel.Id;
        moderator.PromptMessageId = prompt.Id;
        moderator.PromptVersion = ticket.Version;
        await db.SaveChangesAsync(token);
    }

    private async Task RefreshPromptAsync(TicketModerator moderator, Ticket? ticket, CancellationToken token)
    {
        try
        {
            if (await ((IDiscordClient)client.Rest).GetChannelAsync(moderator.ChannelId!.Value, options: TicketBoundary.Options(token)) is not
                IDMChannel channel)
                return;

            if (ticket?.Hidden != false)
            {
                await channel.DeleteMessageAsync(moderator.PromptMessageId!.Value, TicketBoundary.Options(token));
            }
            else
            {
                await channel.ModifyMessageAsync(
                    moderator.PromptMessageId!.Value,
                    properties =>
                    {
                        properties.Content = ticket.Complete ? "Ticket details saved." : "Use **Edit** to complete this ticket.";
                        properties.Embed = TicketPresentation.Describe(ticket);
                        properties.Components = TicketDmModule.Buttons(ticket);
                        properties.AllowedMentions = AllowedMentions.None;
                    },
                    TicketBoundary.Options(token));
            }
        }
        catch (HttpException exception) when (exception.HttpCode is NotFound or Forbidden)
        {
        }
    }
}