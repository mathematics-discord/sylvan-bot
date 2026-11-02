namespace SylvanBot.Features.Automod;

internal static class AutomodReply
{
    public static string WithRefreshStatus(string response, bool refreshed) =>
        refreshed
            ? response
            : response + " Database operation completed, but runtime refresh failed; the previous configuration remains active. Retry a management change or restart the bot.";

    public static async Task SendAsync(IDiscordInteraction interaction, string text, bool ephemeral = true)
    {
        // Keep user content literal, encoding backticks so they cannot close a code block.
        var safeText = text.Replace("`", "\\u0060", StringComparison.Ordinal);
        var chunks = MessageChunker.SplitMessageIntoChunks(safeText, chunkSize: 1980).Value;
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
