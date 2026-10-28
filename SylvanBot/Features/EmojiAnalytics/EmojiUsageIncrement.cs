using System.Runtime.InteropServices;

namespace SylvanBot.Features.EmojiAnalytics;

[StructLayout(LayoutKind.Auto)]
public readonly record struct EmojiUsageIncrement(ulong GuildId, ulong EmojiId, int MessageCount, int ReactionCount);
