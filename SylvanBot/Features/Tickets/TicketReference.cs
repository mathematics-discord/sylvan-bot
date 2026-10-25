namespace SylvanBot.Features.Tickets;

using System.Globalization;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public static class TicketReference
{
    public static TicketMessage ParseMessageLink(string value, ulong guildId)
    {
        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            var validHost = uri.Host is "discord.com" or "discordapp.com" or "www.discord.com"
                or "ptb.discord.com" or "canary.discord.com";

            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var validLink = validHost && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                && parts.Length == 4 && string.Equals(parts[0], "channels", StringComparison.Ordinal)
                && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var linkGuild)
                && linkGuild == guildId;

            if (validLink)
            {
                var channelValid = ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var channelId);
                var messageValid = ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var msgId);
                var validMessage = channelValid && messageValid && channelId != 0 && msgId != 0;

                if (validMessage)
                    return new TicketMessage(channelId, msgId);
            }
        }

        throw new BotCommandException("Provide a Discord message link in this server.");
    }

    public static ulong ParseUser(string value)
    {
        var digits = value.Trim();

        if (digits.StartsWith("<@", StringComparison.Ordinal) && digits.EndsWith('>'))
            digits = digits[2..^1].TrimStart('!');

        if (ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0)
            return id;

        throw new BotCommandException("Provide a user mention or a nonzero Discord user ID.");
    }
}