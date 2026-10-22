using System.Net;

namespace RatBot.Features.EmojiYoink;

public sealed class DiscordGuildEmojiImporter(HttpClient httpClient, ILogger logger)
{
    private const int MaximumEmojiBytes = 256 * 1024;
    private readonly ILogger _logger = logger.ForContext<DiscordGuildEmojiImporter>();

    public async Task<GuildEmote> ImportAsync(
        IGuild guild,
        IUser moderator,
        Emote source,
        CancellationToken ct)
    {
        var destinationName = EmojiName.Normalize(source.Name);

        _logger.Debug(
            "Importing emoji in guild {GuildId} for moderator {ModeratorUserId} ({ModeratorUsername}) from source {SourceEmojiId} "
            + "named {SourceEmojiName} as {DestinationEmojiName}; animated={IsAnimated}",
            guild.Id,
            moderator.Id,
            moderator.Username,
            source.Id,
            source.Name,
            destinationName,
            source.Animated
        );

        await using var imageStream = await DownloadAsync(source, ct).ConfigureAwait(false);
        using var image = new Image(imageStream);
        var options = new RequestOptions
        {
            CancelToken = ct,
            AuditLogReason = $"Emoji yoink by moderator {moderator.Id}; source emoji {source.Id}",
        };

        var created = await guild.CreateEmoteAsync(destinationName, image, options: options)
            .ConfigureAwait(false);

        _logger.Information(
            "Moderator {ModeratorUsername} ({ModeratorUserId}) added emoji {EmojiName} ({CreatedEmojiId}) to guild {GuildId} "
            + "from source {SourceEmojiId}; animated={IsAnimated}",
            moderator.Username,
            moderator.Id,
            created.Name,
            created.Id,
            guild.Id,
            source.Id,
            created.Animated
        );

        return created;
    }

    private async Task<MemoryStream> DownloadAsync(Emote source, CancellationToken ct)
    {
        var animatedQuery = source.Animated ? "?animated=true" : string.Empty;
        var cdnUri = new Uri($"https://cdn.discordapp.com/emojis/{source.Id}.webp{animatedQuery}");
        MemoryStream? imageStream = null;

        try
        {
            using var response = await httpClient
                .GetAsync(cdnUri, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new HttpRequestException(EmojiYoinkMessages.SourceUnavailable, null, HttpStatusCode.NotFound);

            if (!response.IsSuccessStatusCode)
                response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaximumEmojiBytes)
                throw new InvalidDataException(EmojiYoinkMessages.ImageTooLarge);

            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            imageStream = new MemoryStream(MaximumEmojiBytes);
            var buffer = new byte[81920];

            while (true)
            {
                var bytesRead = await responseStream.ReadAsync(buffer, ct).ConfigureAwait(false);

                if (bytesRead == 0)
                    break;

                if (imageStream.Length + bytesRead > MaximumEmojiBytes)
                    throw new InvalidDataException(EmojiYoinkMessages.ImageTooLarge);

                await imageStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            }

            imageStream.Position = 0;
            var completedStream = imageStream;
            imageStream = null;
            return completedStream;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(EmojiYoinkMessages.DownloadFailed, ex);
        }
        finally
        {
            // ReSharper disable once MethodHasAsyncOverload
            imageStream?.Dispose();
        }
    }
}