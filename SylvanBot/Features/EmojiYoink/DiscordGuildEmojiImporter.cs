using System.Net;
using Discord.Net;

namespace SylvanBot.Features.EmojiYoink;

public sealed class DiscordGuildEmojiImporter(HttpClient httpClient, ILogger logger)
{
    private const int MaximumEmojiBytes = 256 * 1024;
    private readonly ILogger _logger = logger.ForContext<DiscordGuildEmojiImporter>();

    public async Task<GuildEmote> ImportAsync(IGuild guild, IUser moderator, Emote source, CancellationToken ct)
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

        GuildEmote created;

        try
        {
            await using MemoryStream imageStream = await DownloadAsync(source: source, ct: ct).ConfigureAwait(false);
            using Image image = new Image(imageStream);

            RequestOptions options = new RequestOptions
            {
                CancelToken = ct, AuditLogReason = $"Emoji yoink by moderator {moderator.Id}; source emoji {source.Id}",
            };

            created = await guild.CreateEmoteAsync(name: destinationName, image: image, options: options).ConfigureAwait(false);
        }
        catch (HttpException exception)
        {
            EmojiImportException error = MapDiscordFailure(exception);
            LogFailure(guild, moderator, source, destinationName, error);
            throw error;
        }
        catch (EmojiImportException error)
        {
            LogFailure(guild, moderator, source, destinationName, error);
            throw;
        }

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

    private static EmojiImportException MapDiscordFailure(HttpException exception)
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

    private void LogFailure(IGuild guild, IUser moderator, Emote source, string destinationName, EmojiImportException error)
    {
        _logger.Warning(
            error,
            "Emoji import failed in guild {GuildId} for moderator {ModeratorUserId} ({ModeratorUsername}) from source {SourceEmojiId} "
            + "named {SourceEmojiName} as {DestinationEmojiName}; animated={IsAnimated}; error code={ErrorCode}; category={ErrorCategory};",
            guild.Id,
            moderator.Id,
            moderator.Username,
            source.Id,
            source.Name,
            destinationName,
            source.Animated,
            error.Code,
            error.Category
        );
    }

    private async Task<MemoryStream> DownloadAsync(Emote source, CancellationToken ct)
    {
        var animatedQuery = source.Animated ? "?animated=true" : string.Empty;
        Uri cdnUri = new Uri($"https://cdn.discordapp.com/emojis/{source.Id}.webp{animatedQuery}");
        MemoryStream? imageStream = null;

        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(requestUri: cdnUri, completionOption: HttpCompletionOption.ResponseHeadersRead, cancellationToken: ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw EmojiYoinkErrors.SourceUnavailable;

            if (!response.IsSuccessStatusCode)
                throw EmojiYoinkErrors.DownloadFailed;

            if (response.Content.Headers.ContentLength > MaximumEmojiBytes)
                throw EmojiYoinkErrors.ImageTooLarge;

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            imageStream = new MemoryStream(MaximumEmojiBytes);
            var buffer = new byte[81920];

            while (true)
            {
                var bytesRead = await responseStream.ReadAsync(buffer: buffer, cancellationToken: ct).ConfigureAwait(false);

                if (bytesRead == 0)
                    break;

                if (imageStream.Length + bytesRead > MaximumEmojiBytes)
                    throw EmojiYoinkErrors.ImageTooLarge;

                await imageStream.WriteAsync(buffer.AsMemory(start: 0, length: bytesRead), cancellationToken: ct).ConfigureAwait(false);
            }

            imageStream.Position = 0;
            MemoryStream completedStream = imageStream;
            imageStream = null;

            return completedStream;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw EmojiYoinkErrors.DownloadFailed;
        }
        catch (HttpRequestException)
        {
            throw EmojiYoinkErrors.DownloadFailed;
        }
        catch (IOException)
        {
            throw EmojiYoinkErrors.DownloadFailed;
        }
        finally
        {
#pragma warning disable MA0042
            // do NOT fuckin dispose this async GRRRR
            // ReSharper disable once MethodHasAsyncOverload
            imageStream?.Dispose();
#pragma warning restore MA0042
        }
    }
}