namespace SylvanBot.Features.Tickets;

using System.Globalization;
using System.Text;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Infrastructure.Data;

public sealed partial class TicketWorker
{
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

            List<ulong> users = await dbContext.Tickets
                .Where(ticket => ticket.Completion == TicketCompletion.Pending && ticket.ModeratorUserId != null
                                 && enabledGuilds.Contains(ticket.GuildId))
                .Select(ticket => ticket.ModeratorUserId!.Value)
                .Union(moderatorsWithDelivery)
                .Where(userId => userId > afterUserId)
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

        // Read the previous prompt's replies before selecting or delivering the next ticket.
        if (moderator.ActiveTicketId is not null && moderator.ChannelId is not null && moderator.LastReadMessageId is not null)
        {
            await ReadRepliesAsync(moderator, cancellationToken);
            await dbContext.Entry(moderator).ReloadAsync(cancellationToken);
        }

        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.ModeratorUserId == userId && ticket.Completion == TicketCompletion.Pending
                             && enabledGuilds.Contains(ticket.GuildId))
            .OrderBy(ticket => ticket.CreatedAtUtc)
            .ThenBy(ticket => ticket.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
        {
            await DeletePromptAsync(moderator.ChannelId, moderator.PromptMessageId, cancellationToken);
            moderator.ClearDelivery();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var changedHead = moderator.ActiveTicketId != ticket.Id;
        var due = changedHead
                  || moderator.NextDeliveryUtc is null
                  || moderator.NextDeliveryUtc <= DateTimeOffset.UtcNow
                  || moderator.LastAttemptedVersion != ticket.Version;

        if (!due)
            return;

        var previousChannelId = moderator.ChannelId;
        var previousPromptId = moderator.PromptMessageId;
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

        var actor = await discord.GetActorAsync(ticket.GuildId, userId, cancellationToken);

        if (!ticket.CanCorrect(actor))
            throw new BotCommandException("The moderator no longer has permission to complete this ticket.");

        await workflow.ShowAsync(actor, ticket.Number, ticket.Hidden, cancellationToken);
        var user = await client.Rest.GetUserAsync(userId, Options(cancellationToken));

        if (user is null || user.IsBot)
            return;

        var channel = await user.CreateDMChannelAsync(Options(cancellationToken));
        var content = await DeliveryTextAsync(actor, ticket, cancellationToken);
        var prompt = await channel.SendMessageAsync(content, embed: TicketPresentation.Describe(ticket),
            options: Options(cancellationToken), allowedMentions: AllowedMentions.None);

        var stillCurrent = await dbContext.Tickets
            .AnyAsync(current => current.Id == ticket.Id && current.GuildId == ticket.GuildId
                                 && current.Version == ticket.Version && current.Completion == TicketCompletion.Pending
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
        moderator.LastReadMessageId = changedHead ? prompt.Id : moderator.LastReadMessageId ?? prompt.Id;
        moderator.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await DeletePromptAsync(previousChannelId, previousPromptId, cancellationToken);
    }

    private async Task<string> DeliveryTextAsync(TicketActor actor, Ticket ticket, CancellationToken cancellationToken)
    {
        var guildName = client.GetGuild(ticket.GuildId)?.Name ?? ticket.GuildId.ToString(CultureInfo.InvariantCulture);
        var text = new StringBuilder()
            .AppendLine($"**{guildName} — Ticket #{ticket.Number}**")
            .AppendLine(TicketPresentation.CompletionText(ticket))
            .AppendLine("Send ordinary DM text to complete this ticket, for example `1 day 8 hours, breaking rules`.")
            .AppendLine(ticket.Action == TicketAction.Timeout
                ? "The observed timeout duration is already filled in; a comment alone retains it. Explicit duration edits change the live timeout."
                : "Use `permanent, comment` for a permanent ban. Finite durations schedule an unban.")
            .AppendLine("Durations run from the original action time. An elapsed duration expires an active action immediately.")
            .AppendLine($"Alternatively, start your message with `{ticket.GuildId}/{ticket.Number} `, or use `/ticket set` in that server.");

        if (ticket.SubjectUserId is not null)
        {
            var history = await workflow.ListAsync(actor, ticket.SubjectUserId.Value, cancellationToken: cancellationToken);
            List<Ticket> related = history.Items
                .Where(previous => previous.Id != ticket.Id)
                .Take(5)
                .ToList();

            if (related.Count > 0)
            {
                text.AppendLine("Recent related tickets:");

                foreach (var previous in related)
                {
                    var link = previous.Hidden ? null : TicketPresentation.Link(previous.GuildId, previous.ChannelId, previous.MessageId);
                    var reference = link is null ? $"#{previous.Number}" : $"[#{previous.Number}]({link})";
                    text.AppendLine(CultureInfo.InvariantCulture, $"• {reference}: {TicketPresentation.ActionName(previous.Action)} · {previous.Status}");
                }
            }
        }

        return text.ToString();
    }

    private async Task ReadRepliesAsync(TicketModerator moderator, CancellationToken cancellationToken)
    {
        var channel = await ((IDiscordClient)client.Rest).GetChannelAsync(moderator.ChannelId!.Value,
            options: Options(cancellationToken)) as IDMChannel;

        if (channel is null)
            return;

        List<IMessage> messages = [];
        ulong? beforeId = null;

        while (true)
        {
            IAsyncEnumerable<IReadOnlyCollection<IMessage>> request = beforeId is null
                ? channel.GetMessagesAsync(100, options: Options(cancellationToken))
                : channel.GetMessagesAsync(beforeId.Value, Direction.Before, 100, options: Options(cancellationToken));

            IEnumerable<IMessage> page = await request.FlattenAsync();

            List<IMessage> ordered = page
                .OrderByDescending(message => message.Id)
                .ToList();

            if (ordered.Count == 0)
                break;

            IEnumerable<IMessage> unread = ordered
                .Where(message => message.Id > moderator.LastReadMessageId);

            messages.AddRange(unread);

            if (ordered.Count < 100 || ordered[^1].Id <= moderator.LastReadMessageId)
                break;

            beforeId = ordered[^1].Id;
        }

        List<IMessage> chronological = messages
            .OrderBy(message => message.Id)
            .ToList();

        foreach (var message in chronological)
        {
            if (message.Author.Id == moderator.UserId && !message.Author.IsBot)
                await ProcessReplyAsync(moderator.UserId, channel, message, cancellationToken);

            await AdvanceCursorAsync(moderator.UserId, message.Id, cancellationToken);
        }
    }

    private async Task ProcessReplyAsync(ulong userId, IDMChannel channel, IMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var moderator = await dbContext.TicketModerators
                .AsNoTracking()
                .SingleAsync(moderator => moderator.UserId == userId, cancellationToken);

            var ticket = await dbContext.Tickets
                .AsNoTracking()
                .SingleOrDefaultAsync(ticket => ticket.Id == moderator.ActiveTicketId && ticket.ModeratorUserId == userId, cancellationToken);

            if (ticket is null || ticket.Completion != TicketCompletion.Pending)
                throw new BotCommandException("That ticket is no longer awaiting completion. This answer was not applied to another ticket.");

            var text = message.Content.Trim();
            var prefix = $"{ticket.GuildId}/{ticket.Number} ";
            var firstWord = text.Split(' ', 2)[0];
            var identityParts = firstWord.Split('/');
            var hasExplicitReference = identityParts.Length == 2
                                       && ulong.TryParse(identityParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)
                                       && long.TryParse(identityParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _);
            ulong? referenceId = message.Reference?.MessageId.IsSpecified == true ? message.Reference.MessageId.Value : null;

            if (hasExplicitReference && !text.StartsWith(prefix, StringComparison.Ordinal))
                throw new BotCommandException("That reference does not identify the currently delivered ticket. No ticket was changed.");

            if (referenceId is not null && referenceId != moderator.PromptMessageId)
                throw new BotCommandException("That reply references an older or different prompt. No ticket was changed.");

            if (hasExplicitReference)
                text = text[prefix.Length..];

            var details = TicketDetails.ParseReply(text);
            var actor = new TicketActor(ticket.GuildId, userId, false, false);
            var reply = new TicketReply(channel.Id, message.Id, referenceId, ticket.Version);
            var change = await workflow.SetModerationAsync(actor, ticket.Number, details, reply, cancellationToken);
            await SendOutcomeAsync(channel, TicketPresentation.Change(change), cancellationToken);
        }
        catch (BotCommandException exception)
        {
            await SendOutcomeAsync(channel, exception.Message, cancellationToken);
        }
    }

    private async Task AdvanceCursorAsync(ulong userId, ulong messageId, CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var moderator = await dbContext.TicketModerators
            .SingleAsync(moderator => moderator.UserId == userId, cancellationToken);

        if (moderator.LastReadMessageId is null || messageId > moderator.LastReadMessageId)
        {
            moderator.LastReadMessageId = messageId;
            moderator.Version++;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SendOutcomeAsync(IDMChannel channel, string content, CancellationToken cancellationToken)
    {
        try
        {
            await channel.SendMessageAsync(content, options: Options(cancellationToken), allowedMentions: AllowedMentions.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning("Ticket DM outcome delivery failed in channel {ChannelId} ({FailureType})", channel.Id, exception.GetType().Name);
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
