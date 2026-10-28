using System.Globalization;
using System.Numerics;

namespace SylvanBot.Application.Features.WorkingHours;

public static class WorkingHoursResponses
{
    public static string? Rejection(WorkingHoursWindow window, string username) =>
        Evaluate(window: window, username: username, includeAccepted: false).Rejection;

    public static IReadOnlyList<string> Accepted(WorkingHoursWindow window) =>
        Evaluate(window: window, username: string.Empty, includeAccepted: true).Messages;

    private static (string? Rejection, IReadOnlyList<string> Messages) Evaluate(WorkingHoursWindow window, string username, bool includeAccepted)
    {
        // Exact decimal arithmetic belongs to response policy, not scheduler execution.
        var startFraction = window.StartText.Length > 8 ? window.StartText[9..] : string.Empty;
        var endFraction = window.EndText.Length > 8 ? window.EndText[9..] : string.Empty;
        var digits = Math.Max(val1: startFraction.Length, val2: endFraction.Length);
        var scale = BigInteger.Pow(value: 10, exponent: digits);
        var start = Units(clock: window.StartText, fraction: startFraction, digits: digits, scale: scale);
        var end = Units(clock: window.EndText, fraction: endFraction, digits: digits, scale: scale);
        var duration = end - start;

        if (duration < 0)
            duration += 86400 * scale;

        if (duration < 60 * scale)
        {
            // Usernames are text, never mentions or code-block delimiters.
            var plain = username
               .Replace("`", newValue: string.Empty, comparisonType: StringComparison.Ordinal)
               .Replace("\r", newValue: string.Empty, comparisonType: StringComparison.Ordinal)
               .Replace("\n", newValue: string.Empty, comparisonType: StringComparison.Ordinal);

            return ($"```text\n@{plain} is not in the time file.\nThis incident will be reported.\n```", []);
        }

        if (duration < 600 * scale)
            return ("VERY FUNNY. AREN'T YOU FUNNY. HEHEHEHE.", []);

        if (duration < 1200 * scale)
            return ("Okay nah that's too low vro", []);

        if (!includeAccepted)
            return (null, []);

        var overlap = BigInteger.Zero;

        for (var day = -1; day <= 1; day++)
        {
            var from = BigInteger.Max(left: start, ((day * 86400) + (22 * 3600)) * scale);
            var to = BigInteger.Min(start + duration, (((day + 1) * 86400) + (7 * 3600)) * scale);

            if (to > from)
                overlap += to - from;
        }

        return (null, AcceptedMessages(duration: duration, overlap: overlap, end: end, scale: scale));
    }

    private static IReadOnlyList<string> AcceptedMessages(BigInteger duration, BigInteger overlap, BigInteger end, BigInteger scale)
    {
        List<string> messages = [];

        if (duration < 3600 * scale)
        {
            messages.Add(
                "Your availability declaration has been accepted. However, People & Culture has identified a concerning discrepancy between your revolutionary potential and your proposed contribution.\n\n"
                + "Under the principles of Ratgirlist-Marxist-Juche Thought, each moderator is encouraged to exercise self-reliance, demonstrate collective responsibility, and voluntarily exceed their mandatory commitments. Please reflect on whether your availability meaningfully advances the emancipation of the posting masses.\n\n"
                + "A positive attitude is no substitute for material participation, comrade. We look forward to seeing this reflected in your next declaration."
            );
        }
        else
            messages.Add("Your availability declaration has been accepted.");

        var fullNight = duration > 0 && overlap == duration && duration <= 9 * 3600 * scale;

        if (fullNight)
        {
            messages.Add(
                "Thank you for volunteering for the night shift, comrade. While the masses sleep, you shall safeguard the means of moderation. Your sacrifice will be recognised through the allocation of additional responsibilities."
            );
        }
        else if (end > 22 * 3600 * scale || end < 7 * 3600 * scale)
            messages.Add("Your commitment has been noted. Your judgement has been questioned. It is past 10pm. Go to fucking bed.");

        AddStatutoryNotices(messages: messages, duration: duration, overlap: overlap, scale: scale);

        messages.Add(
            "Your availability has nevertheless been approved. Human Resources would like to remind you that statutory compliance remains your personal responsibility, especially where management has explicitly authorised the violation."
        );

        return messages;
    }

    private static void AddStatutoryNotices(List<string> messages, BigInteger duration, BigInteger overlap, BigInteger scale)
    {
        if (duration > 6 * 3600 * scale)
        {
            messages.Add(
                "HR records your failure to provide a 20-minute uninterrupted rest break, away from the workstation where applicable, under [regulation 12(1), (3)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/12) of the Working Time Regulations 1998 (SI 1998/1833)."
            );
        }

        if (duration * 7 > 48 * 3600 * scale)
        {
            messages.Add(
                "Your repeating roster exceeds the average weekly 48-hour limit under [regulation 4(1)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/4) of the Working Time Regulations 1998 (SI 1998/1833). HR has recorded the offence against the normal 17-week reference period; one long week alone does not establish the average."
            );
        }

        if (duration > 13 * 3600 * scale)
        {
            messages.Add(
                "Under UK employment law, **11 consecutive hours of rest in each 24-hour period** are required. Your declared working hours are therefore in legal violation of **[regulation 10(1) of the Working Time Regulations 1998 (SI 1998/1833)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/10)**."
            );
        }

        messages.Add(
            "HR charges your seven-day roster with omitting uninterrupted weekly rest: 24 hours per seven days, or the fortnightly alternatives of two uninterrupted 24-hour periods or one uninterrupted 48-hour period, under [regulation 11(1), (2)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/11) of the Working Time Regulations 1998 (SI 1998/1833)."
        );

        var regularNightWorker = overlap >= 3 * 3600 * scale;

        if (regularNightWorker)
        {
            messages.Add(
                "Your daily roster includes at least three hours in our agreed 22:00–07:00 night period. HR classifies you as a regular night worker under [regulation 2(1)'s definitions](https://www.legislation.gov.uk/uksi/1998/1833/regulation/2) of the Working Time Regulations 1998 (SI 1998/1833); this is our agreed period, not the statutory default."
            );

            if (duration > 8 * 3600 * scale)
            {
                messages.Add(
                    "HR charges you with excess average night-worker hours per 24-hour period under [regulation 6(1)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/6) of the Working Time Regulations 1998 (SI 1998/1833). The charge concerns your whole daily duty, not merely its night-time portion."
                );
            }

            messages.Add(
                "HR has failed to offer your required free health assessment before assignment and at regular intervals under [regulation 7(1)(a), (b)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/7) of the Working Time Regulations 1998 (SI 1998/1833). This is an opportunity for assessment, not a compulsory medical examination. HR has nevertheless assigned responsibility for this omission to you."
            );
        }
    }

    private static BigInteger Units(string clock, string fraction, int digits, BigInteger scale)
    {
        var seconds =
            (int.Parse(clock.AsSpan(start: 0, length: 2), provider: CultureInfo.InvariantCulture) * 3600)
            + (int.Parse(clock.AsSpan(start: 3, length: 2), provider: CultureInfo.InvariantCulture) * 60)
            + int.Parse(clock.AsSpan(start: 6, length: 2), provider: CultureInfo.InvariantCulture);

        var fractionalUnits =
            digits == 0
                ? BigInteger.Zero
                : BigInteger.Parse(fraction.PadRight(totalWidth: digits, paddingChar: '0'), provider: CultureInfo.InvariantCulture);

        return (seconds * scale) + fractionalUnits;
    }
}