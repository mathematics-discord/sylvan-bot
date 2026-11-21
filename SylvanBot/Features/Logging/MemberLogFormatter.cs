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
            MemberLogEvent.Join => "Member join",
            MemberLogEvent.Leave => "Member leave",
            MemberLogEvent.NicknameChange => "Nickname change",
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };

        var nickname = entry.Event == MemberLogEvent.NicknameChange
            ? $"{FormatNickname(entry.PreviousNickname)} → {FormatNickname(entry.Nickname)}"
            : FormatNickname(entry.Nickname);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{label} | <@{entry.UserId}> | ID={entry.UserId} | username={Quote(entry.Username)} | nickname={nickname} | {
                entry.OccurredAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
    }

    private static string FormatNickname(Optional<string> nickname) =>
        !nickname.HasValue ? "unavailable" : nickname.Value.Length == 0 ? "unset" : Quote(nickname.Value);

    private static string Quote(string value)
    {
        StringBuilder result = new StringBuilder("\"");

        foreach (var character in value)
        {
            UnicodeCategory category = char.GetUnicodeCategory(character);

            var unsafeControl = char.IsControl(character)
                                || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Format;

            if (unsafeControl)

                result
                    .Append("\\u")
                    .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));

            else if (character is '<' or '>' or '@')

                result.Append(character switch { '<' => '＜', '>' => '＞', _ => '＠' });

            else
            {
                if (character is '\\' or '"' or '*' or '_' or '~' or '`' or '|' or '[' or ']' or '(' or ')' or '#')
                    result.Append('\\');

                result.Append(character);
            }
        }

        return result
            .Append('"')
            .ToString();
    }
}