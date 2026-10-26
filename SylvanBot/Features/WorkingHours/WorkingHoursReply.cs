namespace SylvanBot.Features.WorkingHours;

internal static class WorkingHoursReply
{
    public static async Task SendAsync(IDiscordInteraction interaction, string message)
    {
        var first = true;

        while (message.Length > 0)
        {
            var length = Math.Min(message.Length, 2000);

            if (length < message.Length)
            {
                var boundary = message.LastIndexOf('\n', length - 1, length);

                if (boundary > 0)
                    length = boundary;
            }

            var content = message[..length];

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
            {
                await interaction.FollowupAsync(content, ephemeral: true, allowedMentions: AllowedMentions.None);
            }

            message = message[length..].TrimStart('\n');
        }
    }
}