namespace SylvanBot.Features.EmojiYoink;

public static class EmojiYoinkErrors
{
    public static Error GuildOnly =>
        Error.Validation("EmojiYoink.GuildOnly", "This command can only be used in a server.");

    public static Error InvalidCustomEmoji =>
        Error.Validation("EmojiYoink.InvalidCustomEmoji",
            "Provide exactly one custom server emoji, such as <:rat:123456789012345678>.");

    public static Error InvalidMessageEmoji =>
        Error.Validation("EmojiYoink.InvalidMessageEmoji", "The message must contain exactly one custom server emoji.");

    public static Error InvalidDestinationName =>
        Error.Validation(
            "EmojiYoink.InvalidDestinationName",
            "The emoji name must become 2–32 ASCII letters, digits, or underscores after removing a Discord disambiguation suffix."
        );

    public static Error SourceAlreadyInGuild => Error.Conflict("EmojiYoink.SourceAlreadyInGuild",
        "That emoji already belongs to this server.");

    public static Error SourceUnavailable =>
        Error.NotFound("EmojiYoink.SourceUnavailable", "Discord could not find that emoji image.");

    public static Error ImageTooLarge => Error.Validation("EmojiYoink.ImageTooLarge",
        "That emoji image exceeds Discord's 256 KiB upload limit.");

    public static Error DownloadFailed =>
        Error.Failure("EmojiYoink.DownloadFailed", "Discord could not download that emoji image. Please try again.");

    public static Error BotMissingPermission =>
        Error.Forbidden("EmojiYoink.BotMissingPermission", "Sylvan Bot does not have permission to create server emojis.");

    public static Error InvalidUpload =>
        Error.Validation("EmojiYoink.InvalidUpload", "Discord rejected that emoji image or name.");

    public static Error NoEmojiSlots => Error.Conflict("EmojiYoink.NoEmojiSlots",
        "This server has no available emoji slots for that emoji.");

    public static Error ImportFailed =>
        Error.Failure("EmojiYoink.ImportFailed", "Discord could not create the emoji. Please try again.");
}