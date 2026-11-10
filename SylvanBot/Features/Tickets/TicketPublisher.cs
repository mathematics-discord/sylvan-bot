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
    TicketLocks locks,
    ILogger logger)
{
    // The coordinator holds this ticket's lock while synchronising its publications.
    internal async Task<string> EnsureAsync(ulong guildId, long id, CancellationToken token, bool allowDisabled = false)
    {
        using var timing = new OperationTiming(logger.ForContext("GuildId", guildId).ForContext("TicketId", id), "ticket.publish", "database");

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var config = allowDisabled
                ? await db.TicketConfigurations.SingleAsync(value => value.GuildId == guildId, token)
                : await TicketCoordinator.EnabledAsync(db, guildId, token);

            var ticket = await db.Tickets
               .SingleAsync(value => value.GuildId == guildId && value.Id == id, token);

            List<TicketPublication> references = ticket.Publications
               .Where(reference => !reference.Removed)
               .ToList();

            timing.Next("discord_and_persistence");

            var guild = (IGuild?)client.GetGuild(guildId) ?? await client.Rest.GetGuildAsync(guildId, TicketBoundary.Options(token));
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

                    var incompatible = message.Flags?.HasFlag(MessageFlags.ComponentsV2) == true;

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
                return "\nThe record is hidden; recorded channel copies were removed.";

            var current = ticket.Publications
               .FirstOrDefault(reference => reference is { Current: true, Removed: false });

            if (current is not null)
                return string.Empty;

            var destination = await guild.GetTextChannelAsync(config.PublicationChannel(ticket), options: TicketBoundary.Options(token))
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
        var options = TicketBoundary.Options(token);
        options.RetryMode = RetryMode.RetryRatelimit;
        var embed = TicketPresentation.Describe(ticket);

        if (ticket.Comment.Length <= TicketPresentation.InlineCommentLimit)
            return await channel.SendMessageAsync(embed: embed, options: options, allowedMentions: AllowedMentions.None);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));

        return await channel.SendFileAsync(
            stream,
            $"{ticket.Number}-comment.txt",
            embed: embed,
            allowedMentions: AllowedMentions.None,
            options: options);
    }

    private static async Task UpdateAsync(IMessageChannel channel, ulong messageId, Ticket ticket, CancellationToken token)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));

        await channel.ModifyMessageAsync(
            messageId,
            properties =>
            {
                properties.Content = string.Empty;
                properties.Embeds = new[] { TicketPresentation.Describe(ticket) };
                properties.Components = new ComponentBuilder().Build();

                properties.Attachments = ticket.Comment.Length > TicketPresentation.InlineCommentLimit
                    ? new[] { new FileAttachment(stream, $"{ticket.Number}-comment.txt") }
                    : Array.Empty<FileAttachment>();

                properties.AllowedMentions = AllowedMentions.None;
            },
            TicketBoundary.Options(token));
    }

    public static TicketMessage? FindMessage(TicketActor actor, Ticket ticket)
    {
        if (actor.GuildId != ticket.GuildId)
            throw new BotCommandException("This ticket belongs to another server.");

        // A recorded jump link is navigation, not authorisation to read the ticket.
        // Publication reconciliation owns checking whether the message still exists.
        var reference = ticket.Publications
           .Where(reference => !reference.Removed)
           .OrderByDescending(reference => reference.Current)
           .FirstOrDefault();

        return reference?.Message();
    }

    internal async Task RemindAsync(ulong userId, bool force, CancellationToken token, ulong? requestedGuild = null)
    {
        using var moderatorGate = await locks.ModeratorAsync(userId, token);
        await using var db = await contexts.CreateDbContextAsync(token);

        IQueryable<ulong> enabled = db.TicketConfigurations
           .Where(config => config.Enabled && config.DestinationChannelId != null)
           .Select(config => config.GuildId);

        IQueryable<Ticket> outstanding = db.Tickets
           .Where(value => value.ModeratorUserId == userId
                      && !value.Hidden
                      && !value.ReminderExempt
                      && value.Pending == null
                      && (value.InitialPromptAfterUtc != null
                          || string.IsNullOrWhiteSpace(value.Comment)
                          || (value.Action == TicketAction.Timeout && value.DurationSeconds == null))
                      && enabled.Contains(value.GuildId));

        var now = DateTimeOffset.UtcNow;
        var ticket = await outstanding
           .Where(value => force || value.InitialPromptAfterUtc == null || value.InitialPromptAfterUtc <= now)
           .OrderByDescending(value => value.InitialPromptAfterUtc != null && !force)
           .ThenByDescending(value => value.CreatedAtUtc)
           .ThenByDescending(value => value.Id)
           .FirstOrDefaultAsync(token);

        var moderator = await db.TicketModerators
           .SingleOrDefaultAsync(value => value.UserId == userId, token);

        List<long> ticketIds = [];

        if (ticket is not null)
            ticketIds.Add(ticket.Id);

        if (moderator?.ActiveTicketId is { } activeId)
            ticketIds.Add(activeId);

        using var ticketGates = await locks.TicketsAsync(ticketIds, token);
        var selectedId = ticket?.Id;
        db.ChangeTracker.Clear();

        // The queue may have changed while we waited for an edit. Never refresh a prompt
        // or save its delivery state using a ticket snapshot read before its lock.
        ticket = await outstanding
            .SingleOrDefaultAsync(value => value.Id == selectedId, token);

        moderator = await db.TicketModerators
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

        if (!force && !ticket.InitialPromptPending && !moderator.IsDeliveryDue(ticket.Id, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(token);

            return;
        }

        var config = await TicketCoordinator.EnabledAsync(db, ticket.GuildId, token);

        // Persist backoff before sending, including when the DM outcome is uncertain.
        if (moderator.ActiveTicketId != ticket.Id)
            moderator.PromptMessageId = null;

        moderator.ActiveTicketId = ticket.Id;
        moderator.NextDeliveryUtc = DateTimeOffset.UtcNow.AddSeconds(config.ReminderIntervalSeconds);

        if (ticket.InitialPromptPending)
            ticket.InitialPromptAfterUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, config.ReminderIntervalSeconds));

        moderator.Version++;
        await db.SaveChangesAsync(token);
        var actor = await boundary.GetActorAsync(ticket.GuildId, userId, token);
        var guild = await client.Rest.GetGuildAsync(ticket.GuildId, TicketBoundary.Options(token));
        if (!ticket.CanCorrect(actor))
            throw new BotCommandException("You no longer have access to complete this ticket.");

        var user = await client.Rest.GetUserAsync(userId, TicketBoundary.Options(token));
        var channel = await user.CreateDMChannelAsync(TicketBoundary.Options(token));

        var prompt = await channel.SendMessageAsync(
            $"**{guild.Name}** · Use **Edit** to review or adjust this ticket.",
            embed: TicketPresentation.Describe(ticket),
            components: TicketDmModule.Buttons(ticket),
            options: TicketBoundary.Options(token),
            allowedMentions: AllowedMentions.None);

        ticket.InitialPromptAfterUtc = null;
        moderator.NextDeliveryUtc = DateTimeOffset.UtcNow.AddSeconds(config.ReminderIntervalSeconds);
        moderator.ChannelId = channel.Id;
        moderator.PromptMessageId = prompt.Id;
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning("Ticket prompt refresh failed for moderator {ModeratorId} ({FailureType})", moderator.UserId, exception.GetType().Name);
        }
    }
}
