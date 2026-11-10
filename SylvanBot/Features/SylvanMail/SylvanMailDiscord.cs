using System.Globalization;
using System.Text;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.Tickets;
using static SylvanBot.Features.SylvanMail.SylvanMailRequests;

namespace SylvanBot.Features.SylvanMail;

using DotNext;

public sealed class SylvanMailDiscord(
    DiscordSocketClient client,
    SylvanMailClient mail,
    HttpClient http,
    IDbContextFactory<BotDbContext> contexts,
    ILogger logger) : ISylvanMailDiscord
{
    private const int MaxAttachmentBytes = 8 * 1024 * 1024;

    public void RequireReady(SylvanMailDestinationKind destination)
    {
        if (destination == SylvanMailDestinationKind.MemberDm)
            mail.RequireReady();
        else if (destination != SylvanMailDestinationKind.StaffThread)
            throw new ArgumentOutOfRangeException(nameof(destination));
        else if (client.ConnectionState != ConnectionState.Connected)
            throw new BotCommandException("Not relayed: main Sylvan is disconnected.");
    }

    private DiscordSocketClient ClientFor(SylvanMailDestinationKind destination) => destination switch
    {
        SylvanMailDestinationKind.MemberDm => mail.Client,
        SylvanMailDestinationKind.StaffThread => client,
        _ => throw new ArgumentOutOfRangeException(nameof(destination)),
    };

    public async Task<string> InboundPrefixAsync(ulong guildId, SylvanMailMessage message, CancellationToken ct)
    {
        IGuildUser? member;

        try
        {
            member = await client.Rest.GetGuildUserAsync(guildId: guildId, id: message.AuthorId, Options(ct));
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMember)
        {
            member = null;
        }

        var name = member is null ? Format.Sanitize(message.Username) : $"<@{message.AuthorId}>";
        var timestamp = message.Timestamp.UtcDateTime.ToString("HH:mm:ss", provider: CultureInfo.InvariantCulture);

        return $"[{timestamp}] {name}: ";
    }

    public async Task PingAfterGapAsync(ulong guildId, ulong roleId, ulong threadId, SylvanMailMessage message, CancellationToken ct)
    {
        try
        {
            var dm = await MessageChannelAsync(client: mail.Client, channelId: message.SourceChannelId, ct: ct);
            var before = message.SourceMessageId;

            while (true)
            {
                IEnumerable<IMessage> page = await dm.GetMessagesAsync(
                        fromMessageId: before,
                        dir: Direction.Before,
                        limit: 100,
                        mode: CacheMode.AllowDownload,
                        Options(ct))
                   .FlattenAsync();

                IMessage? previous = null;
                var oldest = before;

                foreach (var item in page)
                {
                    oldest = Math.Min(val1: oldest, val2: item.Id);

                    var isNewerInbound =
                        item.Author.Id == message.AuthorId && item.Source == MessageSource.User && (previous is null || item.Id > previous.Id);

                    if (isNewerInbound)
                        previous = item;
                }

                var reachedStart = oldest == before;

                if (previous is not null || reachedStart)
                {
                    var recentInbound = previous is not null && message.Timestamp - previous.Timestamp < TimeSpan.FromHours(3);

                    if (recentInbound)
                        return;

                    var thread = await MessageChannelAsync(client: client, channelId: threadId, ct: ct);
                    var everyone = roleId == guildId;
                    var mentions = everyone ? new AllowedMentions(AllowedMentionTypes.Everyone) : new AllowedMentions { RoleIds = [roleId] };
                    var text = everyone ? "@everyone" : $"<@&{roleId}>";

                    await AcceptedAsync(() => thread.SendMessageAsync(text: text, options: Options(ct), allowedMentions: mentions));

                    return;
                }

                before = oldest;
            }
        }
        catch (Exception exception)
        {
            // A ping failure is separate from delivery of the user's message. Never resend the payload for it.
            LogFailure(logger: logger, "inbound gap ping", sourceId: message.SourceMessageId, exception: exception);
        }
    }

    public async Task<ulong> OpenDmAsync(ulong userId, CancellationToken ct)
    {
        mail.RequireReady();
        var user = await mail.Client.Rest.GetUserAsync(id: userId, Options(ct));

        if (user is null)
            throw new BotCommandException("Discord could not resolve the recipient; no reply was sent.");

        var channel = await AcceptedAsync(() => user.CreateDMChannelAsync(Options(ct)));

        return channel.Id;
    }

    public async Task<ulong> SendTextAsync(SylvanMailDestination destination, string text, CancellationToken ct)
    {
        RequireReady(destination.Kind);
        var channel = await MessageChannelAsync(client: ClientFor(destination.Kind), channelId: destination.ChannelId, ct: ct);
        var sent = await AcceptedAsync(() => channel.SendMessageAsync(text: text, options: Options(ct), allowedMentions: AllowedMentions.None));

        return sent.Id;
    }

    public async Task SendTicketListAsync(ulong guildId, ulong threadId, ulong userId, CancellationToken ct)
    {
        RequireReady(SylvanMailDestinationKind.StaffThread);
        await using var db = await contexts.CreateDbContextAsync(ct);

        var enabled = await db.TicketConfigurations
           .AsNoTracking()
           .AnyAsync(config => config.GuildId == guildId && config.Enabled && config.DestinationChannelId != null, ct);

        if (!enabled)
            return;

        List<Ticket> tickets = await db.Tickets
           .AsNoTracking()
           .Where(ticket => ticket.GuildId == guildId && ticket.SubjectUserId == userId)
           .OrderByDescending(ticket => ticket.CreatedAtUtc)
           .ThenByDescending(ticket => ticket.Id)
           .Take(11)
           .ToListAsync(ct);

        var hasMore = tickets.Count > 10;

        if (hasMore)
            tickets.RemoveAt(10);

        var channel = await MessageChannelAsync(client, threadId, ct);

        if (channel is not IThreadChannel thread || thread.GuildId != guildId)
            throw new BotCommandException("The SylvanMail thread is not available in this server.");

        var guild = thread.Guild;
        var text = new StringBuilder();

        foreach (var ticket in tickets)
        {
            if (ticket.Hidden)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"**Hidden:** #{ticket.Number}");
                continue;
            }

            Optional<TicketMessage> publication = await FindTicketMessageAsync(guild, ticket, ct);

            text.Append(
                CultureInfo.InvariantCulture,
                $"**#{ticket.Number}** | {TicketPresentation.ActionName(ticket.Action)} | <@{ticket.ModeratorUserId}>");

            if (publication.TryGet(out var message))
                text.Append(CultureInfo.InvariantCulture, $" | {TicketPresentation.Link(guildId, message.ChannelId, message.MessageId)}");

            text.AppendLine();
        }

        if (tickets.Count == 0)
            text.AppendLine("No matching tickets.");

        text.Append(hasMore ? $"More tickets: /ticket show user:{userId}" : "End of results.");

        var embed = new EmbedBuilder()
           .WithTitle($"Tickets | {userId} | page 1")
           .WithDescription(text.ToString())
           .Build();

        await AcceptedAsync(() => channel.SendMessageAsync(embed: embed, options: Options(ct), allowedMentions: AllowedMentions.None));
    }

    private static async Task<Optional<TicketMessage>> FindTicketMessageAsync(IGuild guild, Ticket ticket, CancellationToken ct)
    {
        IOrderedEnumerable<TicketPublication> publications = ticket.Publications
           .Where(publication => !publication.Removed)
           .OrderByDescending(publication => publication.Current);

        foreach (var publication in publications)
        {
            try
            {
                var channel = await guild.GetTextChannelAsync(publication.ChannelId, options: Options(ct));

                if (channel is not null && await channel.GetMessageAsync(publication.MessageId, options: Options(ct)) is not null)
                    return new Optional<TicketMessage>(publication.Message());
            }
            catch (HttpException exception) when (exception.HttpCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
            {
                // A missing or inaccessible publication does not hide the ticket from the list.
            }
        }

        return Optional<TicketMessage>.None;
    }

    public async Task<ulong> SendAttachmentAsync(
        SylvanMailDestination destination,
        SylvanMailAttachment attachment,
        byte[] bytes,
        string label,
        CancellationToken ct)
    {
        RequireReady(destination.Kind);
        var channel = await MessageChannelAsync(client: ClientFor(destination.Kind), channelId: destination.ChannelId, ct: ct);
        await using var stream = new MemoryStream(buffer: bytes, writable: false);
        using var file = new FileAttachment(stream: stream, Path.GetFileName(attachment.Filename), isSpoiler: attachment.Spoiler);

        var sent = await AcceptedAsync(() => channel.SendFilesAsync(
                                           [file],
                                           text: label,
                                           options: Options(ct),
                                           allowedMentions: AllowedMentions.None));

        return sent.Id;
    }

    public async Task<byte[]> DownloadAsync(SylvanMailAttachment attachment, CancellationToken ct)
    {
        // Only Discord attachment hosts. Never log signed URLs, HTTP exception messages or file content.
        var validUrl =
            Uri.TryCreate(uriString: attachment.Url, uriKind: UriKind.Absolute, out var uri)
            && string.Equals(a: uri.Scheme, "https", comparisonType: StringComparison.OrdinalIgnoreCase)
            && (
                string.Equals(a: uri.Host, "cdn.discordapp.com", comparisonType: StringComparison.OrdinalIgnoreCase)
                || string.Equals(a: uri.Host, "media.discordapp.net", comparisonType: StringComparison.OrdinalIgnoreCase));

        if (!validUrl)
            throw new BotCommandException("Not relayed: an attachment does not use a supported Discord attachment host.");

        try
        {
            using var response = await http.GetAsync(
                requestUri: uri,
                completionOption: HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: ct);

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxAttachmentBytes)
                throw new BotCommandException("Not relayed: an attachment is unavailable or exceeds 8 MiB.");

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = new MemoryStream();
            var buffer = new byte[81920];
            var count = await source.ReadAsync(buffer: buffer, cancellationToken: ct);

            while (count > 0)
            {
                if (destination.Length + count > MaxAttachmentBytes)
                    throw new BotCommandException("Not relayed: an attachment exceeds 8 MiB.");

                await destination.WriteAsync(buffer.AsMemory(start: 0, length: count), cancellationToken: ct);
                count = await source.ReadAsync(buffer: buffer, cancellationToken: ct);
            }

            return destination.Length != attachment.Size
                ? throw new BotCommandException("Not relayed: an attachment download was incomplete or its size changed.")
                : destination.ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new BotCommandException("Not relayed: an attachment download failed.", innerException: ex);
        }
    }

    public async Task<Optional<ulong>> ReportAsync(
        SylvanMailMessage source,
        Optional<ulong> threadId,
        Optional<ulong> receiptId,
        string detail,
        CancellationToken ct)
    {
        var toStaff = source.GuildId is null;

        if (toStaff)
        {
            try
            {
                const string guidance =
                    "If the error describes a problem with your text or attachments, fix it and send a new DM. "
                    + "If SylvanMail is busy, try again later. For partial or uncertain delivery, or any problem you cannot fix yourself, "
                    + "DM <@203864799870844928> directly or contact another member of staff. Ask them to check what arrived before resending.";

                // Main may be disconnected while the dedicated client can still explain the failure.
                mail.RequireDmReady();
                var dm = await MessageChannelAsync(client: mail.Client, channelId: source.SourceChannelId, ct: ct);

                await AcceptedAsync(() => dm.SendMessageAsync(
                                        text: $"{detail}\n\n{guidance}",
                                        options: Options(ct),
                                        allowedMentions: AllowedMentions.None));
            }
            catch (Exception exception)
            {
                LogFailure(logger: logger, "user failure notice", sourceId: source.SourceMessageId, exception: exception);
            }
        }

        if (threadId is not { HasValue: true, Value: var destinationThread })
            return receiptId;

        try
        {
            var channel = await MessageChannelAsync(client: client, channelId: destinationThread, ct: ct);
            var report = $"Delivery error · source `{source.SourceMessageId}`: {detail}";

            if (receiptId is { HasValue: true, Value: var existingReceipt })
            {
                await channel.ModifyMessageAsync(
                    messageId: existingReceipt,
                    properties =>
                    {
                        properties.Content = report;
                        properties.AllowedMentions = AllowedMentions.None;
                    },
                    Options(ct));
            }
            else
            {
                var reference = toStaff
                    ? null
                    : new MessageReference(messageId: source.SourceMessageId, channelId: source.SourceChannelId, failIfNotExists: false);

                var sent = await AcceptedAsync(() =>
                                                   channel.SendMessageAsync(
                                                       text: report,
                                                       options: Options(ct),
                                                       allowedMentions: AllowedMentions.None,
                                                       messageReference: reference));

                receiptId = new Optional<ulong>(sent.Id);
            }
        }
        catch (Exception exception)
        {
            LogFailure(logger: logger, "receipt", sourceId: source.SourceMessageId, exception: exception);
        }

        return receiptId;
    }

    public async Task<Optional<ulong>> AcknowledgeAsync(SylvanMailMessage source, bool first, CancellationToken ct)
    {
        try
        {
            mail.RequireDmReady();
            var channel = await MessageChannelAsync(client: mail.Client, channelId: source.SourceChannelId, ct: ct);

            var text = first
                ? "Delivered to staff. Please be patient, someone will get to you soon.\n\n"
                + "Your DMs to SylvanMail reach staff in a private forum post. Staff replies are sent by SylvanMail after the author confirms them; "
                + "edits and deletions after sending are not relayed."
                : "Delivered.";

            var reference = new MessageReference(messageId: source.SourceMessageId, channelId: source.SourceChannelId, failIfNotExists: false);

            var sent = await AcceptedAsync(() =>
                                               channel.SendMessageAsync(
                                                   text: text,
                                                   options: Options(ct),
                                                   allowedMentions: AllowedMentions.None,
                                                   messageReference: reference));

            return new Optional<ulong>(sent.Id);
        }
        catch (Exception exception)
        {
            LogFailure(logger: logger, "acknowledgement", sourceId: source.SourceMessageId, exception: exception);

            return default;
        }
    }
}