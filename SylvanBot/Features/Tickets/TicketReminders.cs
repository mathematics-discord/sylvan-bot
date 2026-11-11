using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Tickets;

using DotNext;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketReminders(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    TicketBoundary boundary,
    TicketLocks locks,
    TicketDeliveryBackoff backoff,
    ILogger logger)
{
    internal async Task RemindAsync(ulong userId, bool force, CancellationToken token, ulong? requestedGuild = null, Optional<TicketActor> resolvedActor = default)
    {
        if (backoff.IsModeratorDeferred(userId))
            return;

        try
        {
            await DeliverAsync(userId, force, token, requestedGuild, resolvedActor);
        }
        catch (Exception exception) when (exception is RateLimitedException || exception is HttpException { HttpCode: TooManyRequests })
        {
            backoff.ModeratorFailed(userId);
            logger.Information("Ticket reminder preparation deferred for moderator {ModeratorId}", userId);
        }
    }

    private async Task DeliverAsync(ulong userId, bool force, CancellationToken token, ulong? requestedGuild = null, Optional<TicketActor> resolvedActor = default)
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

            if (old?.Hidden != false || moderator.NeedsPromptRefresh(old.Version))
            {
                if (!await RefreshPromptAsync(moderator, old, token))
                    return;

                moderator.PromptRevision = old?.Version;
            }
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

        if (moderator.ChannelId is { } knownChannel && backoff.IsDeferred(knownChannel))
            return;

        var config = await TicketCoordinator.EnabledAsync(db, ticket.GuildId, token);

        // Persist backoff before sending, including when the DM outcome is uncertain.
        if (moderator.ActiveTicketId != ticket.Id)
        {
            moderator.PromptMessageId = null;
            moderator.PromptRevision = null;
        }

        moderator.ActiveTicketId = ticket.Id;
        moderator.NextDeliveryUtc = DateTimeOffset.UtcNow.AddSeconds(config.ReminderIntervalSeconds);

        if (ticket.InitialPromptPending)
            ticket.InitialPromptAfterUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, config.ReminderIntervalSeconds));

        moderator.Version++;
        await db.SaveChangesAsync(token);
        var guild = client.GetGuild(ticket.GuildId)
            ?? throw new BotCommandException("Server data is not ready. Try again shortly.");
        var user = await boundary.GetMemberAsync(ticket.GuildId, userId, token)
            ?? throw new BotCommandException("The moderator is no longer available.");
        var actor = resolvedActor.TryGet(out var suppliedActor) ? suppliedActor : TicketBoundary.Actor(user);
        if (!ticket.CanCorrect(actor))
            throw new BotCommandException("You no longer have access to complete this ticket.");

        var channel = client.GetChannel(moderator.ChannelId ?? 0) as IDMChannel
            ?? await user.CreateDMChannelAsync(backoff.ModeratorOptions(userId, token));

        if (backoff.IsDeferred(channel.Id))
            return;

        IUserMessage prompt;

        try
        {
            prompt = await channel.SendMessageAsync(
                $"**{guild.Name}** · Use **Edit** to review or adjust this ticket.",
                embed: TicketPresentation.Describe(ticket),
                components: TicketDmModule.Buttons(ticket),
                options: backoff.Options(channel.Id, token),
                allowedMentions: AllowedMentions.None);
        }
        catch (Exception exception) when (exception is RateLimitedException || exception is HttpException { HttpCode: TooManyRequests })
        {
            backoff.Failed(channel.Id);
            moderator.NextDeliveryUtc = DateTimeOffset.UtcNow.Add(backoff.Remaining(channel.Id));
            await db.SaveChangesAsync(token);
            return;
        }

        backoff.Succeeded(channel.Id);
        backoff.ModeratorSucceeded(userId);
        ticket.InitialPromptAfterUtc = null;
        moderator.NextDeliveryUtc = DateTimeOffset.UtcNow.AddSeconds(config.ReminderIntervalSeconds);
        moderator.ChannelId = channel.Id;
        moderator.PromptMessageId = prompt.Id;
        moderator.PromptRevision = ticket.Version;
        await db.SaveChangesAsync(token);
    }

    private async Task<bool> RefreshPromptAsync(TicketModerator moderator, Ticket? ticket, CancellationToken token)
    {
        try
        {
            if (backoff.IsDeferred(moderator.ChannelId!.Value))
                return false;

            var channel = client.GetChannel(moderator.ChannelId.Value) as IDMChannel
                ?? await ((IDiscordClient)client.Rest).GetChannelAsync(moderator.ChannelId.Value, options: backoff.Options(moderator.ChannelId.Value, token)) as IDMChannel;

            if (channel is null)
                return false;

            if (ticket?.Hidden != false)
            {
                await channel.DeleteMessageAsync(moderator.PromptMessageId!.Value, backoff.Options(channel.Id, token));
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
                    backoff.Options(channel.Id, token));
            }

            return true;
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            moderator.PromptMessageId = null;
            moderator.PromptRevision = null;
            moderator.NextDeliveryUtc = null;
            return true;
        }
        catch (Exception exception) when (exception is RateLimitedException || exception is HttpException { HttpCode: TooManyRequests })
        {
            backoff.Failed(moderator.ChannelId!.Value);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning("Ticket prompt refresh failed for moderator {ModeratorId} ({FailureType})", moderator.UserId, exception.GetType().Name);
            return false;
        }
    }
}
