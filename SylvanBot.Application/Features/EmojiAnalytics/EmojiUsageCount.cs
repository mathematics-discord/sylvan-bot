namespace SylvanBot.Features.EmojiAnalytics;

/// <summary>
///     Represents persisted usage metrics for an emoji in a guild.
/// </summary>
public sealed class EmojiUsageCount
{
    /// <summary>
    ///     Gets the snowflake ID for the emoji.
    /// </summary>
    public required ulong EmojiId { get; init; }

    /// <summary>
    ///     Gets the Identifier of the Guild whence came the emoji.
    /// </summary>
    public required ulong GuildId { get; init; }

    /// <summary>
    ///     Gets or sets the number of times the emoji has been used in a message.
    /// </summary>
    public required int MessageUsageCount { get; set; }

    /// <summary>
    ///     Gets or sets the number of times the emoji has been used as a reaction.
    /// </summary>
    public required int ReactionUsageCount { get; set; }
}