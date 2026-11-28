namespace SylvanBot.Features.EmojiYoink;

internal static class EmojiYoinkErrors
{
    public static EmojiImportException GuildOnly => new EmojiImportException(
        "EmojiYoink.GuildOnly",
        "Validation",
        "This command can only be used in a server.");

    public static EmojiImportException InvalidCustomEmoji => new EmojiImportException(
        "EmojiYoink.InvalidCustomEmoji",
        "Validation",
        "Provide exactly one custom server emoji, such as <:rat:123456789012345678>.");

    public static EmojiImportException InvalidMessageEmoji => new EmojiImportException(
        "EmojiYoink.InvalidMessageEmoji",
        "Validation",
        "The message must contain exactly one custom server emoji.");

    public static EmojiImportException InvalidDestinationName => new EmojiImportException(
        "EmojiYoink.InvalidDestinationName",
        "Validation",
        "The emoji name must become 2–32 ASCII letters, digits, or underscores after removing a Discord disambiguation suffix.");

    public static EmojiImportException SourceAlreadyInGuild => new EmojiImportException(
        "EmojiYoink.SourceAlreadyInGuild",
        "Conflict",
        "That emoji already belongs to this server.");

    public static EmojiImportException SourceUnavailable => new EmojiImportException(
        "EmojiYoink.SourceUnavailable",
        "NotFound",
        "Discord could not find that emoji image.");

    public static EmojiImportException ImageTooLarge => new EmojiImportException(
        "EmojiYoink.ImageTooLarge",
        "Validation",
        "That emoji image exceeds Discord's 256 KiB upload limit.");

    public static EmojiImportException DownloadFailed => new EmojiImportException(
        "EmojiYoink.DownloadFailed",
        "Failure",
        "Discord could not download that emoji image. Please try again.");

    public static EmojiImportException BotMissingPermission => new EmojiImportException(
        "EmojiYoink.BotMissingPermission",
        "Forbidden",
        "Sylvan Bot does not have permission to create server emojis.");

    public static EmojiImportException InvalidUpload => new EmojiImportException(
        "EmojiYoink.InvalidUpload",
        "Validation",
        "Discord rejected that emoji image or name.");

    public static EmojiImportException NoEmojiSlots => new EmojiImportException(
        "EmojiYoink.NoEmojiSlots",
        "Conflict",
        "This server has no available emoji slots for that emoji.");

    public static EmojiImportException ImportFailed => new EmojiImportException(
        "EmojiYoink.ImportFailed",
        "Failure",
        "Discord could not create the emoji. Please try again.");
}
internal sealed class EmojiImportException(string code, string category, string description) : Exception(description)
{
    public string Code { get; } = code;

    public string Category { get; } = category;
}
