namespace SylvanBot.Features.Tickets;

using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed partial class TicketWorker
{
    private static async Task PrepareDeliveryAsync(BotDbContext dbContext, TicketModerator moderator, Ticket ticket, bool changedHead, CancellationToken cancellationToken)
    {
        var interval = await dbContext.TicketConfigurations
            .Where(configuration => configuration.GuildId == ticket.GuildId)
            .Select(configuration => configuration.ReminderIntervalSeconds)
            .SingleAsync(cancellationToken);

        moderator.ActiveTicketId = ticket.Id;

        if (changedHead)
        {
            moderator.PromptMessageId = null;
            moderator.PromptVersion = null;
        }

        // Persist backoff even for blocked DMs or an uncertain send.
        moderator.NextDeliveryUtc = DateTimeOffset.UtcNow.AddSeconds(interval);
        moderator.LastAttemptedVersion = ticket.Version;
        moderator.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessModeratorsAsync(CancellationToken cancellationToken)
    {
        ulong afterUserId = 0;

        while (true)
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            IQueryable<ulong> enabledGuilds = dbContext.TicketConfigurations
                .Where(configuration => configuration.Enabled && configuration.DestinationChannelId != null)
                .Select(configuration => configuration.GuildId);

            IQueryable<ulong> moderatorsWithDelivery = dbContext.TicketModerators
                .Where(moderator => moderator.ActiveTicketId != null || moderator.PromptMessageId != null
                                    || moderator.PromptVersion != null || moderator.LastAttemptedVersion != null
                                    || moderator.NextDeliveryUtc != null)
                .Select(moderator => moderator.UserId);

            var cursor = afterUserId;

            List<ulong> users = await dbContext.Tickets
                .Where(ticket => !ticket.Hidden && ticket.Completion == TicketCompletion.Pending && ticket.ModeratorUserId != null
                                 && !ticket.HidePending && ticket.PendingOperationId == null && enabledGuilds.Contains(ticket.GuildId))
                .Select(ticket => ticket.ModeratorUserId!.Value)
                .Union(moderatorsWithDelivery)
                .Where(userId => userId > cursor)
                .OrderBy(userId => userId)
                .Take(100)
                .ToListAsync(cancellationToken);

            if (users.Count == 0)
                return;

            foreach (var userId in users)
            {
                try
                {
                    await ProcessModeratorAsync(userId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Warning("Ticket DM recovery failed for moderator {ModeratorId} ({FailureType})",
                        userId, exception.GetType().Name);
                }
            }

            afterUserId = users[^1];
        }
    }

    private async Task ProcessModeratorAsync(ulong userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var moderator = await dbContext.TicketModerators
            .SingleOrDefaultAsync(moderator => moderator.UserId == userId, cancellationToken);

        if (moderator is null)
        {
            moderator = new TicketModerator { UserId = userId };
            dbContext.TicketModerators.Add(moderator);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        IQueryable<ulong> enabledGuilds = dbContext.TicketConfigurations
            .Where(configuration => configuration.Enabled && configuration.DestinationChannelId != null)
            .Select(configuration => configuration.GuildId);

        if (moderator.ActiveTicketId is not null)
        {
            var activeGuildEnabled = await dbContext.Tickets
                .AnyAsync(ticket => ticket.Id == moderator.ActiveTicketId && enabledGuilds.Contains(ticket.GuildId), cancellationToken);

            if (!activeGuildEnabled)
            {
                await DeletePromptAsync(moderator.ChannelId, moderator.PromptMessageId, cancellationToken);
                moderator.ClearDelivery();
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.ModeratorUserId == userId && !ticket.Hidden && ticket.Completion == TicketCompletion.Pending
                             && !ticket.HidePending && ticket.PendingOperationId == null && enabledGuilds.Contains(ticket.GuildId))
            .OrderBy(ticket => ticket.CreatedAtUtc)
            .ThenBy(ticket => ticket.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
        {
            await RefreshPromptAsync(moderator, cancellationToken);
            moderator.ClearDelivery();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await DeliverPromptAsync(dbContext, moderator, ticket, enabledGuilds, cancellationToken);
    }

    private async Task DeliverPromptAsync(BotDbContext dbContext, TicketModerator moderator, Ticket ticket, IQueryable<ulong> enabledGuilds, CancellationToken cancellationToken)
    {
        var changedHead = moderator.ActiveTicketId != ticket.Id;
        var due = moderator.IsDeliveryDue(ticket.Id, DateTimeOffset.UtcNow);

        if (!due)
        {
            if (moderator.PromptVersion != ticket.Version)
            {
                await RefreshPromptAsync(moderator, cancellationToken);
                moderator.PromptVersion = ticket.Version;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var userId = moderator.UserId;
        await RefreshPromptAsync(moderator, cancellationToken);
        await PrepareDeliveryAsync(dbContext, moderator, ticket, changedHead, cancellationToken);

        var actor = await discord.GetActorAsync(ticket.GuildId, userId, cancellationToken);

        if (!ticket.CanCorrect(actor))
            throw new BotCommandException("The moderator no longer has permission to complete this ticket.");

        await workflow.ShowAsync(actor, ticket.Number, ticket.Hidden, cancellationToken);
        var user = await client.Rest.GetUserAsync(userId, Options(cancellationToken));

        if (user?.IsBot != false)
            return;

        var channel = await user.CreateDMChannelAsync(Options(cancellationToken));
        var guildName = client.GetGuild(ticket.GuildId)?.Name ?? "Server";
        var content = $"**{guildName}** ? Use **Edit** to complete this ticket.";
        var prompt = await channel.SendMessageAsync(content, embed: TicketPresentation.Describe(ticket),
            components: TicketDmModule.Buttons(ticket),
            options: Options(cancellationToken), allowedMentions: AllowedMentions.None);

        var stillCurrent = await dbContext.Tickets
            .AnyAsync(current => current.Id == ticket.Id && current.GuildId == ticket.GuildId
                                 && current.Version == ticket.Version && !current.Hidden && current.Completion == TicketCompletion.Pending
                                 && enabledGuilds.Contains(current.GuildId), cancellationToken);

        if (!stillCurrent)
        {
            await DeletePromptAsync(channel.Id, prompt.Id, cancellationToken);
            Wake();
            return;
        }

        moderator.ChannelId = channel.Id;
        moderator.PromptMessageId = prompt.Id;
        moderator.PromptVersion = ticket.Version;
        moderator.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshPromptAsync(TicketModerator moderator, CancellationToken cancellationToken)
    {
        if (moderator.ActiveTicketId is null || moderator.ChannelId is null || moderator.PromptMessageId is null)
            return;

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == moderator.ActiveTicketId, cancellationToken);

        if (ticket is null)
            return;

        if (ticket.Hidden)
        {
            await DeletePromptAsync(moderator.ChannelId, moderator.PromptMessageId, cancellationToken);
            return;
        }

        try
        {
            var channel = await ((IDiscordClient)client.Rest).GetChannelAsync(moderator.ChannelId.Value,
                options: Options(cancellationToken)) as IDMChannel;
            var message = channel is null ? null : await channel.GetMessageAsync(moderator.PromptMessageId.Value, options: Options(cancellationToken)) as IUserMessage;

            if (message?.Author.Id != client.CurrentUser.Id)
                return;

            await message.ModifyAsync(properties =>
            {
                properties.Content = ticket.Completion == TicketCompletion.Pending ? "Use **Edit** to complete this ticket." : "Ticket details saved.";
                properties.Embed = TicketPresentation.Describe(ticket);
                properties.Components = TicketDmModule.Buttons(ticket);
                properties.AllowedMentions = AllowedMentions.None;
            }, Options(cancellationToken));
        }
        catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
        {
            // An unavailable older prompt must not prevent delivery of the next ticket.
        }
    }

    private async Task DeletePromptAsync(ulong? channelId, ulong? messageId, CancellationToken cancellationToken)
    {
        if (channelId is null || messageId is null)
            return;

        try
        {
            var channel = await ((IDiscordClient)client.Rest).GetChannelAsync(channelId.Value,
                options: Options(cancellationToken)) as IDMChannel;
            var message = channel is null ? null : await channel.GetMessageAsync(messageId.Value, options: Options(cancellationToken));

            if (message?.Author.Id == client.CurrentUser.Id)
                await channel!.DeleteMessageAsync(messageId.Value, Options(cancellationToken));
        }
        catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
        {
            // The saved binding is invalidated independently of best-effort cleanup.
        }
    }
}
