namespace SylvanBot.Features.EmojiYoink;

public static class EmojiYoinkErrors
{
    public static OperationFailureException GuildOnly => OperationFailureException.Validation("EmojiYoink.GuildOnly", "This command can only be used in a server.");

    public static OperationFailureException InvalidCustomEmoji =>
        OperationFailureException.Validation("EmojiYoink.InvalidCustomEmoji", "Provide exactly one custom server emoji, such as <:rat:123456789012345678>.");

    public static OperationFailureException InvalidMessageEmoji =>
        OperationFailureException.Validation("EmojiYoink.InvalidMessageEmoji", "The message must contain exactly one custom server emoji.");

    public static OperationFailureException InvalidDestinationName =>
        OperationFailureException.Validation(
            "EmojiYoink.InvalidDestinationName",
            "The emoji name must become 2–32 ASCII letters, digits, or underscores after removing a Discord disambiguation suffix."
        );

    public static OperationFailureException SourceAlreadyInGuild => OperationFailureException.Conflict("EmojiYoink.SourceAlreadyInGuild", "That emoji already belongs to this server.");

    public static OperationFailureException SourceUnavailable => OperationFailureException.NotFound("EmojiYoink.SourceUnavailable", "Discord could not find that emoji image.");

    public static OperationFailureException ImageTooLarge => OperationFailureException.Validation("EmojiYoink.ImageTooLarge", "That emoji image exceeds Discord's 256 KiB upload limit.");

    public static OperationFailureException DownloadFailed =>
        OperationFailureException.Failure("EmojiYoink.DownloadFailed", "Discord could not download that emoji image. Please try again.");

    public static OperationFailureException BotMissingPermission =>
        OperationFailureException.Forbidden("EmojiYoink.BotMissingPermission", "Sylvan Bot does not have permission to create server emojis.");

    public static OperationFailureException InvalidUpload => OperationFailureException.Validation("EmojiYoink.InvalidUpload", "Discord rejected that emoji image or name.");

    public static OperationFailureException NoEmojiSlots => OperationFailureException.Conflict("EmojiYoink.NoEmojiSlots", "This server has no available emoji slots for that emoji.");

    public static OperationFailureException ImportFailed => OperationFailureException.Failure("EmojiYoink.ImportFailed", "Discord could not create the emoji. Please try again.");
}