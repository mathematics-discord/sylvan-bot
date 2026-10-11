using System.Runtime.InteropServices;

namespace RatBot.Features.EmojiAnalytics;

[StructLayout(LayoutKind.Auto)]
public readonly record struct EmojiUsageIncrement(ulong GuildId, ulong EmojiId, int MessageCount, int ReactionCount);