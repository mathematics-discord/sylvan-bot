using System.Text;
using Discord.Net;

namespace RatBot.Features.Tickets;

public sealed class TicketDiscord(IDiscordClient client) : ITicketDiscord
{
    public async Task CheckChannelAsync(
        TicketActor actor, ulong channelId, bool publishing, CancellationToken cancellationToken)
    {
        var options = Options(cancellationToken);
        var gld = await client.GetGuildAsync(actor.GuildId, options: options)
            ?? throw new InvalidOperationException();

        var ch = await GetChannelAsync(actor.GuildId, channelId, cancellationToken);
        var moderator = await gld.GetUserAsync(actor.UserId, options: options);

        if (moderator is null || !moderator.GetPermissions(ch).ViewChannel)
            throw new UnauthorizedAccessException();

        if (!publishing)
            return;

        var bot = await gld.GetCurrentUserAsync(options: options);
        var permissions = bot.GetPermissions(ch);
        var canPublish = permissions is
            { ViewChannel: true, SendMessages: true, EmbedLinks: true, AttachFiles: true, ReadMessageHistory: true };

        if (!canPublish)
            throw new InvalidOperationException();
    }

    public async Task<Option<TicketMessage>> FindMessageAsync(
        TicketActor actor, Ticket ticket, CancellationToken cancellationToken)
    {
        if (actor.GuildId != ticket.GuildId)
            throw new UnauthorizedAccessException();

        var options = Options(cancellationToken);
        var gld = await client.GetGuildAsync(actor.GuildId, options: options);
        var moderator = gld is null ? null : await gld.GetUserAsync(actor.UserId, options: options);

        if (gld is null || moderator is null)
            return new None();

        var publications = new List<TicketMessage>();

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
            msg = await ch.SendFileAsync(stream, $"{ticket.Number}-comment.txt",
                allowedMentions: AllowedMentions.None, components: components,
                flags: MessageFlags.ComponentsV2, options: Options(cancellationToken));
        }
        else
        {
            msg = await ch.SendMessageAsync(allowedMentions: AllowedMentions.None,
                components: components, flags: MessageFlags.ComponentsV2, options: Options(cancellationToken));
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
        var attachments = new List<FileAttachment>();

        if (oversized)
            attachments.Add(new FileAttachment(stream, $"{ticket.Number}-comment.txt"));

        await ch.ModifyMessageAsync(publication.MessageId, properties =>
        {
            properties.Content = string.Empty;
            properties.Embeds = Array.Empty<Embed>();
            properties.Flags = (msg.Flags ?? MessageFlags.None) | MessageFlags.ComponentsV2;
            properties.Components = TicketPresentation.ChannelMessage(ticket);
            properties.Attachments = attachments;
            properties.AllowedMentions = AllowedMentions.None;
        }, Options(cancellationToken));

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

    private async Task<ITextChannel> GetChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken)
    {
        var gld = await client.GetGuildAsync(guildId, options: Options(cancellationToken));
        var ch = gld is null ? null : await gld.GetTextChannelAsync(channelId, options: Options(cancellationToken));

        if (ch is null || ch.GuildId != guildId || ch.ChannelType != ChannelType.Text)
            throw new InvalidOperationException();

        return ch;
    }

    private static RequestOptions Options(CancellationToken cancellationToken) => new()
    {
        CancelToken = cancellationToken,
        RetryMode = RetryMode.AlwaysFail,
    };
}