namespace SylvanBot.Application.Features.SylvanMail;

public sealed record SylvanMailReply(string Content, bool Anonymous)
{
    public static SylvanMailReply? Parse(string content, ulong botId, bool replyToBot, bool replyPingEnabled)
    {
        var mention = $"<@{botId}>";
        var nicknameMention = $"<@!{botId}>";

        var explicitMention =
            content.Contains(value: mention, comparisonType: StringComparison.Ordinal)
            || content.Contains(value: nicknameMention, comparisonType: StringComparison.Ordinal);

        if (explicitMention)
        {
            var body = content
               .Replace(oldValue: mention, newValue: string.Empty, comparisonType: StringComparison.Ordinal)
               .Replace(oldValue: nicknameMention, newValue: string.Empty, comparisonType: StringComparison.Ordinal)
               .Trim();

            return new SylvanMailReply(Content: body, Anonymous: true);
        }

        return replyToBot && replyPingEnabled ? new SylvanMailReply(Content: content, Anonymous: false) : null;
    }
}