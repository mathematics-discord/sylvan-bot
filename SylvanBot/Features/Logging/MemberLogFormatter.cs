using System.Globalization;
using System.Text;
using SylvanBot.Application.Features.Logging;

namespace SylvanBot.Features.Logging;

using DotNext;

public static class MemberLogFormatter
{
    public static string Format(MemberLogEntry entry)
    {
        var label = entry.Event switch
        {
            MemberLogEvent.Join => "Member Join",
            MemberLogEvent.Leave => "Member Leave",
            MemberLogEvent.NicknameChange => "Nickname Change",
            MemberLogEvent.DisplayNameChange => "Display Name Change",
            MemberLogEvent.UsernameChange => "Username Change",
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"**{label}** [{entry.OccurredAtUtc.UtcDateTime:yyyyMMdd'T'HH:mm:ss}] - <@{entry.UserId}> (`{entry.UserId}`, {Code(entry.Username)})");

        return entry.Event is MemberLogEvent.Join or MemberLogEvent.Leave
            ? header
            : $"{header}: {FormatName(entry.PreviousName)} \u2192 {FormatName(entry.Name)}";
    }

    private static string FormatName(Optional<string> name) =>
        !name.HasValue ? "unavailable" : name.Value.Length == 0 ? "unset" : Code(name.Value);

    private static string Code(string value)
    {
        StringBuilder result = new StringBuilder("`");

        foreach (var character in value)
        {
            UnicodeCategory category = char.GetUnicodeCategory(character);

            var unsafeCharacter = char.IsControl(character)
                                  || character == '`'
                                  || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Format;

            if (unsafeCharacter)
                result
                    .Append("\\u")
                    .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else if (character is '<' or '>' or '@')
                result.Append(character switch { '<' => '\uff1c', '>' => '\uff1e', _ => '\uff20' });
            else
                result.Append(character);
        }

        return result
            .Append('`')
            .ToString();
    }
}