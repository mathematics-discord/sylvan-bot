namespace SylvanBot.Features.Automod;

using System.Globalization;
using SylvanBot.Application.Features.Automod;

internal static class AutomodReply
{
    public static (string Text, bool AttachPattern) FormatRule(AutomodRule rule)
    {
        var status = rule.Disabled ? "Disabled" : "Active";
        var action = AutomodRuleList.DescribeAction(rule);
        var details = string.Create(CultureInfo.InvariantCulture, $"**Automod rule #{rule.Id}**\n{status} | {rule.MatchType} | {action}");
        const string opening = "\n```\n";
        const string closing = "\n```";

        var hasControlCharacters = rule.Pattern
            .Any(character => character != '\n' && char.IsControl(character));

        var attachPattern = rule.Pattern.Length > 2000 - details.Length - opening.Length - closing.Length
                            || rule.Pattern.Contains("```", StringComparison.Ordinal)
                            || hasControlCharacters;

        return attachPattern
            ? (details + "\nThe full pattern is attached.", true)
            : (details + opening + rule.Pattern + closing, false);
    }

    public static string WithRefreshStatus(string response, bool refreshed) =>
        refreshed
            ? response
            : response
              + " Database operation completed, but runtime refresh failed; the previous configuration remains active. Retry a management change or restart the bot.";

    public static async Task SendAsync(IDiscordInteraction interaction, string text, bool ephemeral = true)
    {
        // Keep user content literal, encoding backticks so they cannot close a code block.
        var safeText = text.Replace("`", "\\u0060", StringComparison.Ordinal);
        var chunks = MessageChunker.SplitMessageIntoChunks(safeText, chunkSize: 1980);
        var first = true;

        foreach (var chunk in chunks)
        {
            var content = "```\n" + chunk + "\n```";

            if (first)
            {
                await interaction.ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = content;
                    properties.AllowedMentions = AllowedMentions.None;
                });

                first = false;
            }
            else
                await interaction.FollowupAsync(text: content, ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
        }
    }
}
