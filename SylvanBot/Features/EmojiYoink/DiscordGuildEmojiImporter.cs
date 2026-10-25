namespace SylvanBot.Features.EmojiYoink;

using System.Net;
using Discord.Net;

public sealed class DiscordGuildEmojiImporter(HttpClient httpClient, ILogger logger)
{
    private const int MaximumEmojiBytes = 256 * 1024;
    private readonly ILogger _logger = logger.ForContext<DiscordGuildEmojiImporter>();

    public async Task<ErrorOr<GuildEmote>> ImportAsync(IGuild guild, IUser moderator, Emote source,
        CancellationToken ct)
    {
        ErrorOr<string> nameResult = EmojiName.Normalize(source.Name);

        if (nameResult.IsError)
            return nameResult.Errors;

        var destinationName = nameResult.Value;
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

        ErrorOr<MemoryStream> downloadResult = await DownloadAsync(source, ct).ConfigureAwait(false);

        if (downloadResult.IsError)
        {
            LogFailure(guild, moderator, source, destinationName, downloadResult.Errors);
            return downloadResult.Errors;
        }

        await using var imageStream = downloadResult.Value;
        using var image = new Image(imageStream);
        var options = new RequestOptions
        {
            CancelToken = ct,
            AuditLogReason = $"Emoji yoink by moderator {moderator.Id}; source emoji {source.Id}"
        };

        ErrorOr<GuildEmote> result;
        try
        {
            result = await guild.CreateEmoteAsync(destinationName, image, options: options).ConfigureAwait(false);
        }
        catch (HttpException exception)
        {
            result = MapDiscordFailure(exception);
        }

        if (result.IsError)
        {
            LogFailure(guild, moderator, source, destinationName, result.Errors);
            return result.Errors;
        }

        var created = result.Value;
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

    private void LogFailure(IGuild guild, IUser moderator, Emote source, string destinationName, List<Error> errors)
    {
        _logger.Warning(
            "Emoji import failed in guild {GuildId} for moderator {ModeratorUserId} ({ModeratorUsername}) from source {SourceEmojiId} "
            + "named {SourceEmojiName} as {DestinationEmojiName}; animated={IsAnimated}; error codes={ErrorCodes}",
            guild.Id,
            moderator.Id,
            moderator.Username,
            source.Id,
            source.Name,
            destinationName,
            source.Animated,
            errors.Select(error => error.Code).ToArray()
        );
    }

    private async Task<ErrorOr<MemoryStream>> DownloadAsync(Emote source, CancellationToken ct)
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
                return EmojiYoinkErrors.SourceUnavailable;

            if (!response.IsSuccessStatusCode)
                return EmojiYoinkErrors.DownloadFailed;

            if (response.Content.Headers.ContentLength > MaximumEmojiBytes)
                return EmojiYoinkErrors.ImageTooLarge;

            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            imageStream = new MemoryStream(MaximumEmojiBytes);
            var buffer = new byte[81920];

            while (true)
            {
                var bytesRead = await responseStream.ReadAsync(buffer, ct).ConfigureAwait(false);

                if (bytesRead == 0)
                    break;

                if (imageStream.Length + bytesRead > MaximumEmojiBytes)
                    return EmojiYoinkErrors.ImageTooLarge;

                await imageStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            }

            imageStream.Position = 0;
            var completedStream = imageStream;
            imageStream = null;
            return completedStream;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return EmojiYoinkErrors.DownloadFailed;
        }
        catch (HttpRequestException)
        {
            return EmojiYoinkErrors.DownloadFailed;
        }
        catch (IOException)
        {
            return EmojiYoinkErrors.DownloadFailed;
        }
        finally
        {
            // ReSharper disable once MethodHasAsyncOverload
            imageStream?.Dispose();
        }
    }

    private static Error MapDiscordFailure(HttpException exception)
    {
        int? discordCode = exception.DiscordCode is null ? null : (int)exception.DiscordCode;

        if (discordCode == 30008)
            return EmojiYoinkErrors.NoEmojiSlots;

        if (discordCode == 50035 || exception.HttpCode == HttpStatusCode.BadRequest)
            return EmojiYoinkErrors.InvalidUpload;

        if (discordCode == 50013 || exception.HttpCode == HttpStatusCode.Forbidden)
            return EmojiYoinkErrors.BotMissingPermission;

        return EmojiYoinkErrors.ImportFailed;
    }
}
