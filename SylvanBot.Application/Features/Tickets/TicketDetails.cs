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

    public TicketDetails(TicketDurationEdit duration, TicketCommentEdit comment)
    {
        if (duration.Value is null || comment.Value is null)
            throw new BotCommandException("Duration and comment edit choices are required.");

        Duration = duration;
        Comment = comment;
    }

    public TicketDurationEdit Duration { get; init; }
    public TicketCommentEdit Comment { get; private init; }
    public bool HasDuration => Duration.Value is TicketDurationEdit.Set or TicketDurationEdit.Permanent;

    public long DurationSeconds => Duration.Value switch
    {
        TicketDurationEdit.Set { Seconds: > 0 } duration => duration.Seconds,
        TicketDurationEdit.Permanent => long.MaxValue,
        _ => throw new BotCommandException("A positive duration is required."),
    };

    public bool ChangesDuration(long? currentSeconds) => HasDuration
        && DurationSeconds != (currentSeconds ?? long.MaxValue);

    public static TicketDetails ParseReply(string text)
    {
        if (text.Length > Ticket.CommentLimit + 200)
            throw new BotCommandException("The reply is too long. Nothing was changed.");

        var permanent = Permanent.Match(text);

        if (permanent.Success)
            return new TicketDetails(new TicketDurationEdit.Permanent(), ReadComment(text[permanent.Length..]));

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

                var multiplier = string.Equals(unit, "M", StringComparison.Ordinal)
                    ? 2592000
                    : unit.ToLowerInvariant() switch
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

        if (offset == 0 && comment.Value is TicketCommentEdit.Keep)
            throw new BotCommandException("Supply a duration or a nonblank comment. Nothing was changed.");

        return new TicketDetails(offset > 0 ? new TicketDurationEdit.Set(seconds) : new TicketDurationEdit.Keep(), comment);
    }

    public static TicketDetails ParseFields(string? duration, string? comment)
    {
        if (duration is null)
            return new TicketDetails(
                new TicketDurationEdit.Keep(),
                comment is null ? new TicketCommentEdit.Keep() : new TicketCommentEdit.Replace(RequireComment(comment)));

        var details = ParseReply(duration);

        if (!details.HasDuration || details.Comment.Value is not TicketCommentEdit.Keep)
            throw new BotCommandException("The duration field must contain only a duration or 'permanent'. Nothing was changed.");

        return details with { Comment = comment is null ? new TicketCommentEdit.Keep() : new TicketCommentEdit.Replace(RequireComment(comment)) };
    }

    public DateTimeOffset Expiry(DateTimeOffset actionTime)
    {
        if (!HasDuration || DurationSeconds == long.MaxValue)
            return DateTimeOffset.MaxValue;

        try
        {
            return actionTime.AddSeconds(DurationSeconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new BotCommandException("That duration is too large. Nothing was changed.", exception);
        }
    }

    public DateTimeOffset TimeoutExpiry(DateTimeOffset actionTime)
    {
        var expiry = Expiry(actionTime);

        if (expiry == DateTimeOffset.MaxValue)
            throw new BotCommandException("Discord timeouts cannot be permanent. Nothing was changed.");

        return expiry;
    }

    private static TicketCommentEdit ReadComment(string text) => string.IsNullOrWhiteSpace(text)
        ? new TicketCommentEdit.Keep()
        : new TicketCommentEdit.Replace(RequireComment(text));

    private static string RequireComment(string text)
    {
        var comment = text.Trim();

        if (comment.Length == 0 || comment.Length > Ticket.CommentLimit)
            throw new BotCommandException($"Comments must contain 1 to {Ticket.CommentLimit:N0} characters. Nothing was changed.");

        return comment;
    }
}
