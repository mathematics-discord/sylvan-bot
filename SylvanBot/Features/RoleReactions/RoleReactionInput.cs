using System.Globalization;

namespace SylvanBot.Features.RoleReactions;

using DotNext;

internal static class RoleReactionInput
{
    public static string GetEmojiKey(IEmote emote)
    {
        if (emote is Emote custom)
            return "custom:" + custom.Id.ToString(CultureInfo.InvariantCulture);

        var unicode = emote.Name.Replace("\uFE0E", string.Empty, StringComparison.Ordinal).Replace("\uFE0F", string.Empty, StringComparison.Ordinal);

        return "unicode:" + unicode;
    }

    public static Optional<IEmote> ParseEmoji(string text)
    {
        var input = text.Trim();

        if (input.Length is 0 or > 128)
            return default;

        if (Emote.TryParse(input, out var custom))
        {
            var validCustomEmoji = custom.Id != 0 && string.Equals(input, custom.ToString(), StringComparison.Ordinal);

            return validCustomEmoji ? new Optional<IEmote>(custom) : default;
        }

        if (StringInfo.ParseCombiningCharacters(input).Length != 1)
            return default;

        return Emoji.TryParse(input, out var unicode) ? new Optional<IEmote>(unicode) : default;
    }

    public static Optional<MessageLink> ParseMessageLink(string text, ulong guildId)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
            return default;

        var discordHost = uri.Host is "discord.com" or "canary.discord.com" or "ptb.discord.com";

        var supportedAddress =
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && discordHost && uri.IsDefaultPort && uri.UserInfo.Length == 0;

        if (!supportedAddress)
        {
            return default;
        }

        var parts = uri.AbsolutePath.TrimEnd('/').Split('/');

        if (parts.Length != 5 || !string.Equals(parts[1], "channels", StringComparison.Ordinal))
            return default;

        if (!ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var linkedGuildId))
            return default;

        if (!ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var channelId))
            return default;

        if (!ulong.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var messageId))
            return default;

        if (linkedGuildId != guildId || channelId == 0 || messageId == 0)
            return default;

        return new Optional<MessageLink>(new MessageLink(channelId, messageId));
    }

    public sealed record MessageLink(ulong ChannelId, ulong MessageId);
}