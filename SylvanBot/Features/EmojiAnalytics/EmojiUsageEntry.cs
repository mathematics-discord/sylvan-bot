namespace SylvanBot.Features.EmojiAnalytics;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public readonly record struct EmojiUsageEntry(ulong EmojiId, int MessageUsageCount, int ReactionUsageCount);