namespace SylvanBot.Application.Features.Tickets;

using System.Globalization;
using System.Text.RegularExpressions;
using Common;

public sealed record TicketDetails
{
    private static readonly Regex Permanent = new Regex(
        @"\A\s*(?:permanent|perm|p)\b[\s,;:]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex DurationItem = new Regex(
        @"\G\s*(?<amount>[+-]?[0-9]+)\s*(?<unit>seconds?|secs?|s|minutes?|mins?|m|hours?|hrs?|h|days?|d|weeks?|w|months?|years?|y)(?![\p{L}])[\s,;:]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public TicketDetails(bool hasDuration, long? durationSeconds, string? comment)
    {
        HasDuration = hasDuration;
        DurationSeconds = durationSeconds;
        Comment = comment;
    }

    public bool HasDuration { get; init; }

    public long? DurationSeconds { get; init; }

    public string? Comment { get; init; }

    public void Deconstruct(out bool hasDuration, out long? durationSeconds, out string? comment)
    {
        hasDuration = HasDuration;
        durationSeconds = DurationSeconds;
        comment = Comment;
    }

    static public TicketDetails ParseReply(string text)
    {
        if (text.Length > Ticket.CommentLimit + 200)
            throw new BotCommandException("The reply is too long. Nothing was changed.");

        var permanent = Permanent.Match(text);

        if (permanent.Success)
            return new TicketDetails(true, null, ReadComment(text[permanent.Length..]));

        var offset = 0;
        long seconds = 0;

        try
        {
            while (true)
            {
                var match = DurationItem.Match(text, offset);

                if (!match.Success)
                    break;

                var amount = long.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);

                if (amount <= 0)
                    throw new BotCommandException("Duration items must be positive. Nothing was changed.");

                var unit = match.Groups["unit"].Value;
                var multiplier = string.Equals(unit, "M", StringComparison.Ordinal) ? 2592000 : unit.ToLowerInvariant() switch
                {
                    "s" or "sec" or "secs" or "second" or "seconds" => 1,
                    "m" or "min" or "mins" or "minute" or "minutes" => 60,
                    "h" or "hr" or "hrs" or "hour" or "hours" => 3600,
                    "d" or "day" or "days" => 86400,
                    "w" or "week" or "weeks" => 604800,
                    "month" or "months" => 2592000,
                    "y" or "year" or "years" => 31536000,
                    _ => throw new BotCommandException("Unknown duration unit. Nothing was changed."),
                };

                seconds = checked(seconds + checked(amount * multiplier));
                offset = match.Index + match.Length;
            }
        }
        catch (OverflowException exception)
        {
            throw new BotCommandException("That duration is too large. Nothing was changed.", exception);
        }

        var remainder = text[offset..].Trim();
        var startsWithNumber = remainder.Length > 0 && (char.IsDigit(remainder[0]) || remainder[0] is '+' or '-');

        if (startsWithNumber)
            throw new BotCommandException("Use a duration such as '1 day 8 hours, breaking rules'. Nothing was changed.");

        var comment = ReadComment(remainder);

        if (offset == 0 && comment is null)
            throw new BotCommandException("Supply a duration or a nonblank comment. Nothing was changed.");

        return new TicketDetails(offset > 0, offset > 0 ? seconds : null, comment);
    }

    public static TicketDetails ParseFields(string? duration, string? comment)
    {
        if (duration is null)
            return new TicketDetails(false, null, comment is null ? null : RequireComment(comment));

        var details = ParseReply(duration);

        if (!details.HasDuration || details.Comment is not null)
            throw new BotCommandException("The duration field must contain only a duration or 'permanent'. Nothing was changed.");

        return details with { Comment = comment is null ? null : RequireComment(comment) };
    }

    public DateTimeOffset? Expiry(DateTimeOffset actionTime)
    {
        if (!HasDuration || DurationSeconds is null)
            return null;

        try
        {
            return actionTime.AddSeconds(DurationSeconds.Value);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new BotCommandException("That duration is too large. Nothing was changed.", exception);
        }
    }

    public DateTimeOffset TimeoutExpiry(DateTimeOffset actionTime, DateTimeOffset now)
    {
        var expiry = Expiry(actionTime)
                     ?? throw new BotCommandException("Discord timeouts cannot be permanent. Nothing was changed.");

        if (expiry > now.AddDays(28))
            throw new BotCommandException("Discord timeouts cannot end more than 28 days from now. Nothing was changed.");

        return expiry;
    }

    private static string? ReadComment(string text) => string.IsNullOrWhiteSpace(text) ? null : RequireComment(text);

    private static string RequireComment(string text)
    {
        var comment = text.Trim();

        if (comment.Length == 0 || comment.Length > Ticket.CommentLimit)
            throw new BotCommandException($"Comments must contain 1 to {Ticket.CommentLimit:N0} characters. Nothing was changed.");

        return comment;
    }
}
