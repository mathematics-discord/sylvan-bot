namespace RatBot.Application.Features.EmojiYoink;

public sealed class EmojiYoinkOperations(IGuildEmojiImporter importer, ILogger logger)
{
    private readonly ILogger _logger = logger.ForContext<EmojiYoinkOperations>();

    public async Task<ErrorOr<CreatedGuildEmoji>> YoinkAsync(YoinkEmojiCommand command, CancellationToken ct = default)
    {
        ErrorOr<EmojiName> nameResult = EmojiName.Create(command.Source.InvokedName);

        if (nameResult.IsError)
            return nameResult.Errors;

        EmojiName destinationName = nameResult.Value;

        _logger.Debug(
            "Importing emoji in guild {GuildId} for moderator {ModeratorUserId} ({ModeratorUsername}) from source {SourceEmojiId} "
                + "named {SourceEmojiName} as {DestinationEmojiName}; animated={IsAnimated}",
            command.GuildId,
            command.ModeratorUserId,
            command.ModeratorUsername,
            command.Source.EmojiId,
            command.Source.InvokedName,
            destinationName.Value,
            command.Source.IsAnimated
        );

        ErrorOr<CreatedGuildEmoji> result = await importer
            .ImportAsync(command.GuildId, command.ModeratorUserId, destinationName, command.Source, ct)
            .ConfigureAwait(false);

        if (result.IsError)
        {
            _logger.Warning(
                "Emoji import failed in guild {GuildId} for moderator {ModeratorUserId} ({ModeratorUsername}) from source {SourceEmojiId} "
                    + "named {SourceEmojiName} as {DestinationEmojiName}; animated={IsAnimated}; error codes={ErrorCodes}",
                command.GuildId,
                command.ModeratorUserId,
                command.ModeratorUsername,
                command.Source.EmojiId,
                command.Source.InvokedName,
                destinationName.Value,
                command.Source.IsAnimated,
                result.Errors.Select(error => error.Code).ToArray()
            );

            return result.Errors;
        }

        CreatedGuildEmoji created = result.Value;

        _logger.Information(
            "Moderator {ModeratorUsername} ({ModeratorUserId}) added emoji {EmojiName} ({CreatedEmojiId}) to guild {GuildId} "
                + "from source {SourceEmojiId}; animated={IsAnimated}",
            command.ModeratorUsername,
            command.ModeratorUserId,
            created.Name,
            created.EmojiId,
            command.GuildId,
            command.Source.EmojiId,
            created.IsAnimated
        );

        return created;
    }
}
