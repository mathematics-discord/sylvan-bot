using System.Net;
using Discord.Net;

namespace SylvanBot.Features.EmojiYoink;

public sealed class DiscordGuildEmojiImporter(HttpClient httpClient, ILogger logger)
{
    private const int MaximumEmojiBytes = 256 * 1024;
    private readonly ILogger _logger = logger.ForContext<DiscordGuildEmojiImporter>();

    public async Task<Result<GuildEmote>> ImportAsync(IGuild guild, IUser moderator, Emote source, CancellationToken ct)
    {
        Result<string> nameResult = EmojiName.Normalize(source.Name);

        if (!nameResult.IsSuccessful)
            return new Result<GuildEmote>(nameResult.Error!);

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

        Result<MemoryStream> downloadResult = await DownloadAsync(source: source, ct: ct).ConfigureAwait(false);

        if (!downloadResult.IsSuccessful)
        {
            LogFailure(guild: guild, moderator: moderator, source: source, destinationName: destinationName, downloadResult.Error!);

            return new Result<GuildEmote>(downloadResult.Error!);
        }

        await using var imageStream = downloadResult.Value;
        using var image = new Image(imageStream);

        var options = new RequestOptions { CancelToken = ct, AuditLogReason = $"Emoji yoink by moderator {moderator.Id}; source emoji {source.Id}" };

        Result<GuildEmote> result;

        try
        {
            result = await guild.CreateEmoteAsync(name: destinationName, image: image, options: options).ConfigureAwait(false);
        }
        catch (HttpException exception)
        {
            result = new Result<GuildEmote>(MapDiscordFailure(exception));
        }

        if (!result.IsSuccessful)
        {
            LogFailure(guild: guild, moderator: moderator, source: source, destinationName: destinationName, result.Error!);

            return new Result<GuildEmote>(result.Error!);
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

    private static OperationFailureException MapDiscordFailure(HttpException exception)
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

    private void LogFailure(IGuild guild, IUser moderator, Emote source, string destinationName, Exception error)
    {
        _logger.Warning(
            "Emoji import failed in guild {GuildId} for moderator {ModeratorUserId} ({ModeratorUsername}) from source {SourceEmojiId} "
            + "named {SourceEmojiName} as {DestinationEmojiName}; animated={IsAnimated}; error code={ErrorCode}; category={ErrorCategory}; description={ErrorDescription}",
            guild.Id,
            moderator.Id,
            moderator.Username,
            source.Id,
            source.Name,
            destinationName,
            source.Animated,
            (error as OperationFailureException)?.Code,
            (error as OperationFailureException)?.Category,
            error.Message
        );
    }

    private async Task<Result<MemoryStream>> DownloadAsync(Emote source, CancellationToken ct)
    {
        var animatedQuery = source.Animated ? "?animated=true" : string.Empty;
        var cdnUri = new Uri($"https://cdn.discordapp.com/emojis/{source.Id}.webp{animatedQuery}");
        MemoryStream? imageStream = null;

        try
        {
            using var response = await httpClient
               .GetAsync(requestUri: cdnUri, completionOption: HttpCompletionOption.ResponseHeadersRead, cancellationToken: ct)
               .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new Result<MemoryStream>(EmojiYoinkErrors.SourceUnavailable);

            if (!response.IsSuccessStatusCode)
                return new Result<MemoryStream>(EmojiYoinkErrors.DownloadFailed);

            if (response.Content.Headers.ContentLength > MaximumEmojiBytes)
                return new Result<MemoryStream>(EmojiYoinkErrors.ImageTooLarge);

            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            imageStream = new MemoryStream(MaximumEmojiBytes);
            var buffer = new byte[81920];

            while (true)
            {
                var bytesRead = await responseStream.ReadAsync(buffer: buffer, cancellationToken: ct).ConfigureAwait(false);

                if (bytesRead == 0)
                    break;

                if (imageStream.Length + bytesRead > MaximumEmojiBytes)
                    return new Result<MemoryStream>(EmojiYoinkErrors.ImageTooLarge);

                await imageStream.WriteAsync(buffer.AsMemory(start: 0, length: bytesRead), cancellationToken: ct).ConfigureAwait(false);
            }

            imageStream.Position = 0;
            var completedStream = imageStream;
            imageStream = null;

            return completedStream;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Result<MemoryStream>(EmojiYoinkErrors.DownloadFailed);
        }
        catch (HttpRequestException)
        {
            return new Result<MemoryStream>(EmojiYoinkErrors.DownloadFailed);
        }
        catch (IOException)
        {
            return new Result<MemoryStream>(EmojiYoinkErrors.DownloadFailed);
        }
        finally
        {
            // ReSharper disable once MethodHasAsyncOverload
            imageStream?.Dispose();
        }
    }
}