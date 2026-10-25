namespace SylvanBot.Features.EmojiAnalytics;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public readonly record struct EmojiUsageIncrement(ulong GuildId, ulong EmojiId, int MessageCount, int ReactionCount);