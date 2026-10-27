namespace SylvanBot.Features.WorkingHours;

internal static class WorkingHoursReply
{
    public static async Task SendAsync(IDiscordInteraction interaction, string message)
    {
        var first = true;

        while (message.Length > 0)
        {
            var length = Math.Min(val1: message.Length, val2: 2000);

            if (length < message.Length)
            {
                var boundary = message.LastIndexOf(value: '\n', length - 1, count: length);

                if (boundary > 0)
                    length = boundary;
            }

            var content = message[..length];

            if (first)
            {
                await interaction.ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content         = content;
                    properties.AllowedMentions = AllowedMentions.None;
                });
                first = false;
            }
            else
                await interaction.FollowupAsync(text: content, ephemeral: true, allowedMentions: AllowedMentions.None);

            message = message[length..].TrimStart('\n');
        }
    }
}