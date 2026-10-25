namespace SylvanBot.Features.EmojiAnalytics;

public readonly record struct EmojiUsagePage(ImmutableArray<EmojiUsageEntry> Items, int Page, int TotalPages);