namespace RatBot.Features.Bridge;

using System.Globalization;

public static class BridgeEligibility
{
    public static bool IsEligible(
        bool isGuildMessage,
        bool isHuman,
        bool isConfigured,
        ulong botUserId,
        string content,
        IReadOnlyCollection<ulong> mentionedUserIds)
    {
        if (!isGuildMessage || !isHuman || !isConfigured || botUserId == 0)
            return false;

        if (!mentionedUserIds.Contains(botUserId))
            return false;

        var id = botUserId.ToString(CultureInfo.InvariantCulture);
        return content.Contains($"<@{id}>", StringComparison.Ordinal)
            || content.Contains($"<@!{id}>", StringComparison.Ordinal);
    }
}