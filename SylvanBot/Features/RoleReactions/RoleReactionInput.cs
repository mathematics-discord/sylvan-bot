using System.Globalization;

namespace SylvanBot.Features.RoleReactions;

internal static class RoleReactionInput
{
    public static string GetEmojiKey(IEmote emote)
    {
        if (emote is Emote custom)
            return "custom:" + custom.Id.ToString(CultureInfo.InvariantCulture);

        var unicode = emote.Name.Replace("\uFE0E", newValue: string.Empty, comparisonType: StringComparison.Ordinal)
           .Replace("\uFE0F", newValue: string.Empty, comparisonType: StringComparison.Ordinal);

        return "unicode:" + unicode;
    }

    public static DotNext.Optional<IEmote> ParseEmoji(string text)
    {
        var input = text.Trim();

        if (input.Length is 0 or > 128)
            return default;

        if (Emote.TryParse(text: input, out var custom))
        {
            var validCustomEmoji = custom.Id != 0 && string.Equals(a: input, custom.ToString(), comparisonType: StringComparison.Ordinal);

            return validCustomEmoji ? new DotNext.Optional<IEmote>(custom) : default;
        }

        if (StringInfo.ParseCombiningCharacters(input).Length != 1)
            return default;

        return Emoji.TryParse(text: input, out var unicode) ? new DotNext.Optional<IEmote>(unicode) : default;
    }

    public static DotNext.Optional<MessageLink> ParseMessageLink(string text, ulong guildId)
    {
        if (!Uri.TryCreate(text.Trim(), uriKind: UriKind.Absolute, out var uri))
            return default;

        var discordHost = uri.Host is "discord.com" or "canary.discord.com" or "ptb.discord.com";

        var supportedAddress =
            string.Equals(a: uri.Scheme, b: Uri.UriSchemeHttps, comparisonType: StringComparison.Ordinal)
            && discordHost
            && uri.IsDefaultPort
            && uri.UserInfo.Length == 0;

        if (!supportedAddress)
            return default;

        var parts = uri.AbsolutePath.TrimEnd('/').Split('/');

        if (parts.Length != 5 || !string.Equals(parts[1], "channels", comparisonType: StringComparison.Ordinal))
            return default;

        if (!ulong.TryParse(parts[2], style: NumberStyles.None, provider: CultureInfo.InvariantCulture, out var linkedGuildId))
            return default;

        if (!ulong.TryParse(parts[3], style: NumberStyles.None, provider: CultureInfo.InvariantCulture, out var channelId))
            return default;

        if (!ulong.TryParse(parts[4], style: NumberStyles.None, provider: CultureInfo.InvariantCulture, out var messageId))
            return default;

        if (linkedGuildId != guildId || channelId == 0 || messageId == 0)
            return default;

        return new DotNext.Optional<MessageLink>(new MessageLink(ChannelId: channelId, MessageId: messageId));
    }

    public sealed record MessageLink(ulong ChannelId, ulong MessageId);
}