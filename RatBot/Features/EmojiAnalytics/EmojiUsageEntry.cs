using System.Runtime.InteropServices;

namespace RatBot.Features.EmojiAnalytics;

[StructLayout(LayoutKind.Auto)]
public readonly record struct EmojiUsageEntry(ulong EmojiId, int MessageUsageCount, int ReactionUsageCount);