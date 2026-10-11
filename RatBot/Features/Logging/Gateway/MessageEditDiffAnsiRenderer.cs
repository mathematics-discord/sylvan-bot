using System.Text;
using RatBot.Application.Features.Logging;

namespace RatBot.Features.Logging.Gateway;

internal static class MessageEditDiffAnsiRenderer
{
    private const string Reset = "\e[0m";
    private const string Red = "\e[1;31m";
    private const string Green = "\e[1;32m";

    public static string RenderBefore(MessageEditDiff diff)
    {
        return RenderSegments(diff.Before);
    }

    public static string RenderAfter(MessageEditDiff diff)
    {
        return RenderSegments(diff.After);
    }

    private static string RenderSegments(IReadOnlyList<MessageEditDiffSegment> segments)
    {
        StringBuilder builder = new StringBuilder();
        AppendSegments(builder, segments);
        return builder.ToString();
    }

    private static void AppendSegments(StringBuilder builder, IReadOnlyList<MessageEditDiffSegment> segments)
    {
        foreach (MessageEditDiffSegment segment in segments)
        {
            Option<string> colorResult = GetColor(segment.Kind);

            if (colorResult.Value is not Some<string>(var color))
            {
                builder.Append(segment.Text);
                continue;
            }

            builder.Append(color);
            builder.Append(segment.Text);
            builder.Append(Reset);
        }
    }

    private static Option<string> GetColor(MessageEditDiffSegmentKind kind)
    {
        return kind switch
        {
            MessageEditDiffSegmentKind.Removed => new Some<string>(Red),
            MessageEditDiffSegmentKind.Added => new Some<string>(Green),
            _ => new None()
        };
    }
}