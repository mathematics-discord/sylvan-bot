using System.Globalization;
using Discord.Net;
using SylvanBot.Application.Features.SylvanMail;
using static SylvanBot.Features.SylvanMail.SylvanMailRequests;

namespace SylvanBot.Features.SylvanMail;

using DotNext;

public sealed class SylvanMailDiscord(DiscordSocketClient client, HttpClient http, ILogger logger) : ISylvanMailDiscord
{
    private const int MaxAttachmentBytes = 8 * 1024 * 1024;

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
            var dm = await MessageChannelAsync(client, channelId: message.SourceChannelId, ct: ct);
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

                if (previous is not null)
                {
                    if (message.Timestamp - previous.Timestamp < TimeSpan.FromHours(3))
                        return;

                    var thread = await MessageChannelAsync(client, channelId: threadId, ct: ct);
                    var everyone = roleId == guildId;

                    var mentions = everyone ? new AllowedMentions(AllowedMentionTypes.Everyone) : new AllowedMentions { RoleIds = [roleId] };
                    var text = everyone ? "@everyone" : $"<@&{roleId}>";
                    await AcceptedAsync(() => thread.SendMessageAsync(text: text, options: Options(ct), allowedMentions: mentions));

                    return;
                }

                if (oldest == before)
                    return;

                before = oldest;
            }
        }
        catch (Exception exception)
        {
            // A ping failure is separate from delivery of the user's message. Never resend the payload for it.
            LogFailure(logger, "inbound gap ping", sourceId: message.SourceMessageId, exception: exception);
        }
    }

    public async Task<ulong> OpenDmAsync(ulong userId, CancellationToken ct)
    {
        var user = await client.Rest.GetUserAsync(id: userId, Options(ct));

        if (user is null)
            throw new BotCommandException("Discord could not resolve the recipient; no reply was sent.");

        var channel = await AcceptedAsync(() => user.CreateDMChannelAsync(Options(ct)));

        return channel.Id;
    }

    public async Task<ulong> SendTextAsync(ulong channelId, string text, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(client, channelId: channelId, ct: ct);
        var sent = await AcceptedAsync(() => channel.SendMessageAsync(text: text, options: Options(ct), allowedMentions: AllowedMentions.None));

        return sent.Id;
    }

    public async Task<ulong> SendAttachmentAsync(ulong channelId, SylvanMailAttachment attachment, byte[] bytes, string label, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(client, channelId: channelId, ct: ct);
        await using var stream = new MemoryStream(buffer: bytes, writable: false);
        using var file = new FileAttachment(stream: stream, Path.GetFileName(attachment.Filename), isSpoiler: attachment.Spoiler);

        var sent = await AcceptedAsync(() =>
            channel.SendFilesAsync([file], text: label, options: Options(ct), allowedMentions: AllowedMentions.None));

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
                : destination
                    .ToArray();
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

                await SendTextAsync(channelId: source.SourceChannelId, $"{detail}\n\n{guidance}", ct: ct);
            }
            catch (Exception exception)
            {
                LogFailure(logger, "user failure notice", sourceId: source.SourceMessageId, exception: exception);
            }
        }

        if (threadId is not { HasValue: true, Value: var destinationThread })
            return receiptId;

        try
        {
            var channel = await MessageChannelAsync(client, channelId: destinationThread, ct: ct);
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
                    channel.SendMessageAsync(text: report, options: Options(ct), allowedMentions: AllowedMentions.None, messageReference: reference));
                receiptId = new Optional<ulong>(sent.Id);
            }
        }
        catch (Exception exception)
        {
            LogFailure(logger, "receipt", sourceId: source.SourceMessageId, exception: exception);
        }

        return receiptId;
    }

    public async Task<Optional<ulong>> AcknowledgeAsync(SylvanMailMessage source, bool first, CancellationToken ct)
    {
        try
        {
            var channel = await MessageChannelAsync(client, channelId: source.SourceChannelId, ct: ct);

            var text = first ? "Delivered to staff. Please be patient, someone will get to you soon." : "Delivered.";

            var reference = new MessageReference(messageId: source.SourceMessageId, channelId: source.SourceChannelId, failIfNotExists: false);

            var sent = await AcceptedAsync(() =>
                channel.SendMessageAsync(text: text, options: Options(ct), allowedMentions: AllowedMentions.None, messageReference: reference));

            return new Optional<ulong>(sent.Id);
        }
        catch (Exception exception)
        {
            LogFailure(logger, "acknowledgement", sourceId: source.SourceMessageId, exception: exception);

            return default;
        }
    }
}