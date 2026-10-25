namespace SylvanBot.Features.Tickets;

using System.Collections.Immutable;
using System.Text;
using Discord.Rest;
using Discord.Net;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed class DiscordTicket(DiscordSocketClient socketClient) : IDiscordTicket
{
    private readonly IDiscordClient client = socketClient;

    public async Task CheckChannelAsync(
        TicketActor actor,
        ulong channelId,
        bool publishing,
        CancellationToken cancellationToken)
    {
        var options = Options(cancellationToken);

        var gld = await socketClient.Rest.GetGuildAsync(actor.GuildId, options)
                  ?? throw new BotCommandException("Sylvan could not access this server.");

        var ch = await GetChannelAsync(actor.GuildId, channelId, cancellationToken);
        var moderator = await gld.GetUserAsync(actor.UserId, options);

        if (moderator?.GetPermissions(ch).ViewChannel != true)
            throw new BotCommandException("You must be able to view the tickets channel to access its records.");

        if (!publishing)
            return;

        await CheckPublicationAsync(actor.GuildId, channelId, cancellationToken);
    }

    public async Task CheckPublicationAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken)
    {
        var ch = await GetChannelAsync(guildId, channelId, cancellationToken);
        var gld = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        var bot = await gld.GetCurrentUserAsync(Options(cancellationToken));
        var permissions = bot.GetPermissions(ch);

        var canPublish = permissions is
            { ViewChannel: true, SendMessages: true, EmbedLinks: true, AttachFiles: true, ReadMessageHistory: true };

        if (!canPublish)
            throw new BotCommandException(
                "Sylvan needs View Channel, Read Message History, Send Messages, Embed Links and Attach Files "
                + "in the tickets channel.");
    }

    public async Task CheckCaptureAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        var bot = await guild.GetCurrentUserAsync(Options(cancellationToken));

        if (!bot.GuildPermissions.ViewAuditLog)
            throw new BotCommandException("Sylvan needs View Audit Log to capture and reconcile moderation actions.");
    }

    public async Task<TicketActor> GetActorAsync(ulong guildId, ulong userId, CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        var member = await guild.GetUserAsync(userId, Options(cancellationToken));

        if (member is null || member.IsBot)
            throw new BotCommandException("The responsible moderator is not an available human member of this server.");

        var permissions = member.GuildPermissions;

        return new TicketActor(guildId, userId, permissions.Administrator, permissions.ModerateMembers,
            permissions.BanMembers, permissions.KickMembers, permissions.MuteMembers, permissions.DeafenMembers,
            permissions.ManageRoles, permissions.ViewAuditLog);
    }

    public async Task<DateTimeOffset?> GetTimeoutAsync(ulong guildId, ulong userId, CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        var member = await guild.GetUserAsync(userId, Options(cancellationToken));
        return member?.TimedOutUntil;
    }

    public async Task SetTimeoutAsync(Ticket ticket, DateTimeOffset? expiry, Guid operationId, CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(ticket.GuildId, Options(cancellationToken));
        var member = ticket.SubjectUserId is null ? null : await guild.GetUserAsync(ticket.SubjectUserId.Value, Options(cancellationToken));

        if (member is null)
            throw new BotCommandException("The member is no longer in this server. The timeout was not changed.");

        var options = Options(cancellationToken);
        options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {operationId:D}";

        try
        {
            await member.ModifyAsync(properties => properties.TimedOutUntil = expiry, options);
        }
        catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.BadRequest
                                               or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            throw new BotCommandException("Discord rejected the timeout change. Check permissions, hierarchy and the current member state.", exception);
        }
    }

    public async Task<bool> IsBannedAsync(ulong guildId, ulong userId, CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        return await guild.GetBanAsync(userId, Options(cancellationToken)) is not null;
    }

    public async Task UnbanAsync(Ticket ticket, Guid operationId, CancellationToken cancellationToken)
    {
        if (ticket.SubjectUserId is null)
            throw new BotCommandException("The subject is unknown. No unban was attempted.");

        var guild = await socketClient.Rest.GetGuildAsync(ticket.GuildId, Options(cancellationToken));
        var options = Options(cancellationToken);
        options.AuditLogReason = $"Sylvan ticket {ticket.Number}; operation {operationId:D}";

        try
        {
            await guild.RemoveBanAsync(ticket.SubjectUserId.Value, options);
        }
        catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.BadRequest
                                               or System.Net.HttpStatusCode.Forbidden)
        {
            throw new BotCommandException("Discord rejected the scheduled unban. Check Sylvan's Ban Members permission.", exception);
        }
    }

    public async Task<Option<TicketMessage>> FindMessageAsync(
        TicketActor actor,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        if (actor.GuildId != ticket.GuildId)
            throw new BotCommandException("This ticket belongs to another server.");

        var options = Options(cancellationToken);
        var gld = await client.GetGuildAsync(actor.GuildId, options: options);
        var moderator = gld is null ? null : await gld.GetUserAsync(actor.UserId, options: options);

        if (gld is null || moderator is null)
            return new None();

        List<TicketMessage> publications = [];

        if (ticket.ChannelId is not null && ticket.MessageId is not null)
            publications.Add(new TicketMessage(ticket.ChannelId.Value, ticket.MessageId.Value));

        if (ticket.PreviousChannelId is not null && ticket.PreviousMessageId is not null)
            publications.Add(new TicketMessage(ticket.PreviousChannelId.Value, ticket.PreviousMessageId.Value));

        foreach (var publication in publications)
        {
            try
            {
                var ch = await gld.GetTextChannelAsync(publication.ChannelId, options: options);

                if (ch is null || !moderator.GetPermissions(ch).ViewChannel)
                    continue;

                // Download on a cache miss: absence from the message cache does not prove deletion.
                var msg = await ch.GetMessageAsync(publication.MessageId, CacheMode.AllowDownload, options);

                if (msg is not null)
                    return new Some<TicketMessage>(publication);
            }
            catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.NotFound
                                                      or System.Net.HttpStatusCode.Forbidden)
            {
                // Keep the association in storage, but do not offer an unavailable jump link.
            }
        }

        return new None();
    }

    public async Task<TicketMessage> PublishAsync(Ticket ticket, ulong channelId, CancellationToken cancellationToken)
    {
        var ch = await GetChannelAsync(ticket.GuildId, channelId, cancellationToken);
        var components = TicketPresentation.ChannelMessage(ticket);
        IUserMessage msg;

        if (ticket.Comment?.Length > TicketPresentation.InlineCommentLimit)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));

            msg = await ch.SendFileAsync(
                stream,
                $"{ticket.Number}-comment.txt",
                options: Options(cancellationToken),
                allowedMentions: AllowedMentions.None,
                components: components,
                flags: MessageFlags.ComponentsV2);
        }
        else
        {
            msg = await ch.SendMessageAsync(
                options: Options(cancellationToken),
                allowedMentions: AllowedMentions.None,
                components: components,
                flags: MessageFlags.ComponentsV2);
        }

        return new TicketMessage(ch.Id, msg.Id);
    }

    public async Task<bool> UpdateAsync(Ticket ticket, TicketMessage publication, CancellationToken cancellationToken)
    {
        var ch = await GetChannelAsync(ticket.GuildId, publication.ChannelId, cancellationToken);
        var msg = await ch.GetMessageAsync(publication.MessageId, options: Options(cancellationToken));

        if (msg is null || msg.Author.Id != client.CurrentUser.Id)
            return false;

        var oversized = ticket.Comment?.Length > TicketPresentation.InlineCommentLimit;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment ?? string.Empty));
        List<FileAttachment> attachments = [];

        if (oversized)
            attachments.Add(new FileAttachment(stream, $"{ticket.Number}-comment.txt"));

        await ch.ModifyMessageAsync(
            publication.MessageId,
            properties =>
            {
                properties.Content = string.Empty;
                properties.Embeds = Array.Empty<Embed>();
                properties.Flags = (msg.Flags ?? MessageFlags.None) | MessageFlags.ComponentsV2;
                properties.Components = TicketPresentation.ChannelMessage(ticket);
                properties.Attachments = attachments;
                properties.AllowedMentions = AllowedMentions.None;
            },
            Options(cancellationToken));

        return true;
    }

    public async Task<bool> RemoveAsync(ulong guildId, TicketMessage publication, CancellationToken cancellationToken)
    {
        var ch = await GetChannelAsync(guildId, publication.ChannelId, cancellationToken);

        try
        {
            var msg = await ch.GetMessageAsync(publication.MessageId, options: Options(cancellationToken));

            if (msg is null)
                return true;

            if (msg.Author.Id != client.CurrentUser.Id)
                return false;

            await ch.DeleteMessageAsync(publication.MessageId, Options(cancellationToken));

            return true;
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            return true;
        }
    }

    private async Task<ITextChannel> GetChannelAsync(
        ulong guildId,
        ulong channelId,
        CancellationToken cancellationToken)
    {
        var gld = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        var ch = gld is null ? null : await gld.GetTextChannelAsync(channelId, options: Options(cancellationToken));

        if (ch is null || ch.GuildId != guildId || ch.ChannelType != ChannelType.Text)
            throw new BotCommandException("The tickets channel is unavailable or is not a text channel in this server.");

        return ch;
    }

    public async Task<(ulong? LastAuditId, ImmutableArray<TicketObservation> Observations)> ReadAuditAsync(
        ulong guildId,
        DateTimeOffset boundary,
        CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        List<RestAuditLogEntry> entries = [];
        ulong? beforeId = null;

        // The installed SDK's paginator advances with beforeId. Read descending pages
        // explicitly rather than combining that paginator with Discord's ascending afterId.
        while (true)
        {
            IEnumerable<RestAuditLogEntry> page = await guild.GetAuditLogsAsync(100, Options(cancellationToken), beforeId: beforeId)
                .FlattenAsync();

            List<RestAuditLogEntry> ordered = page
                .OrderByDescending(entry => entry.Id)
                .ToList();

            if (ordered.Count == 0)
                break;

            foreach (var entry in ordered)
            {
                if (entry.CreatedAt >= boundary)
                    entries.Add(entry);
            }

            if (ordered.Count < 100 || ordered[^1].CreatedAt < boundary)
                break;

            beforeId = ordered[^1].Id;
        }

        List<RestAuditLogEntry> chronological = entries
            .OrderBy(entry => entry.Id)
            .ToList();

        var observations = ImmutableArray.CreateBuilder<TicketObservation>();

        foreach (var entry in chronological)
        {
            var observation = Observe(guildId, entry);

            if (observation is not null)
                observations.Add(observation);
        }

        return (chronological.Count == 0 ? null : chronological[^1].Id, observations.ToImmutable());
    }

    private TicketObservation? Observe(ulong guildId, RestAuditLogEntry entry)
    {
        var actor = entry.User;
        var ownBot = actor?.Id == socketClient.CurrentUser.Id;
        Guid? operationId = null;

        if (ownBot && entry.Reason is not null)
        {
            var marker = entry.Reason.LastIndexOf("; operation ", StringComparison.Ordinal);

            if (marker >= 0 && Guid.TryParseExact(entry.Reason[(marker + 12)..], "D", out var parsed))
                operationId = parsed;
        }

        var permanentBotAction = ownBot
                                 && entry.Reason?.StartsWith("Permanent, Automatic image-burst spam detection:", StringComparison.Ordinal) == true;

        if (entry.Data is BanAuditLogData ban)
        {
            return new TicketObservation(guildId, entry.Id, TicketAction.Ban, ban.Target?.Id, actor?.Id,
                actor?.IsBot == true, entry.Reason, entry.CreatedAt, OperationId: operationId,
                PermanentBotAction: permanentBotAction);
        }

        if (entry.Data is UnbanAuditLogData unban)
        {
            return new TicketObservation(guildId, entry.Id, TicketAction.Ban, unban.Target?.Id, actor?.Id,
                actor?.IsBot == true, entry.Reason, entry.CreatedAt, Removed: true, OperationId: operationId);
        }

        if (entry.Data is not MemberUpdateAuditLogData update)
            return null;

        DateTimeOffset? previous = update.Before.TimedOutUntil;
        DateTimeOffset? expiry = update.After.TimedOutUntil;

        if (previous == expiry)
            return null;

        var removed = expiry is null || expiry <= entry.CreatedAt;

        return new TicketObservation(guildId, entry.Id, TicketAction.Timeout, update.Target?.Id, actor?.Id,
            actor?.IsBot == true, entry.Reason, entry.CreatedAt, removed, previous, expiry, operationId);
    }

    private static RequestOptions Options(CancellationToken cancellationToken) => new()
    {
        CancelToken = cancellationToken,
        RetryMode = RetryMode.AlwaysFail,
    };
}
