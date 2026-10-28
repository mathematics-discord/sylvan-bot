namespace SylvanBot.Application.Features.RoleReactions;

/// <summary>A message reaction that grants a guild role.</summary>
public sealed class RoleReaction
{
    /// <summary>Gets or sets the owning guild.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Gets or sets the message channel.</summary>
    public ulong ChannelId { get; set; }

    /// <summary>Gets or sets the target message.</summary>
    public ulong MessageId { get; set; }

    /// <summary>Gets or sets the stable emoji identity.</summary>
    public string EmojiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the emoji representation used by Discord.</summary>
    public string EmojiText { get; set; } = string.Empty;

    /// <summary>Gets or sets the role granted by this reaction.</summary>
    public ulong RoleId { get; set; }
}