namespace SylvanBot.Application.Features.SylvanMail;

public sealed record SylvanMailReply(string Content, bool Anonymous)
{
    public static SylvanMailReply? Parse(string content, ulong botId, bool replyToBot, bool replyPingEnabled)
    {
        var mention = $"<@{botId}>";
        var nicknameMention = $"<@!{botId}>";
        var explicitMention = content.Contains(mention, StringComparison.Ordinal)
            || content.Contains(nicknameMention, StringComparison.Ordinal);

        if (explicitMention)
        {
            var body = content.Replace(mention, string.Empty, StringComparison.Ordinal)
                .Replace(nicknameMention, string.Empty, StringComparison.Ordinal)
                .Trim();

            return new SylvanMailReply(body, Anonymous: true);
        }

        return replyToBot && replyPingEnabled ? new SylvanMailReply(content, Anonymous: false) : null;
    }
}
