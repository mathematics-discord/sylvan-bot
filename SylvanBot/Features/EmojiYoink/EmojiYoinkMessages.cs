namespace SylvanBot.Features.EmojiYoink;

internal static class EmojiYoinkMessages
{
    internal const string GuildOnly =
        "This command can only be used in a server.";

    internal const string InvalidCustomEmoji =
        "Provide exactly one custom server emoji, such as <:rat:123456789012345678>.";

    internal const string InvalidMessageEmoji =
        "The message must contain exactly one custom server emoji.";

    internal const string InvalidDestinationName =
        "The emoji name must become 2–32 ASCII letters, digits, or underscores after removing a Discord disambiguation suffix.";

    internal const string SourceAlreadyInGuild =
        "That emoji already belongs to this server.";

    internal const string SourceUnavailable =
        "Discord could not find that emoji image.";

    internal const string ImageTooLarge =
        "That emoji image exceeds Discord's 256 KiB upload limit.";

    internal const string DownloadFailed =
        "Discord could not download that emoji image. Please try again.";

    internal const string BotMissingPermission =
        "Sylvan Bot does not have permission to create server emojis.";

    internal const string InvalidUpload =
        "Discord rejected that emoji image or name.";

    internal const string NoEmojiSlots =
        "This server has no available emoji slots for that emoji.";

    internal const string ImportFailed =
        "Discord could not create the emoji. Please try again.";
}