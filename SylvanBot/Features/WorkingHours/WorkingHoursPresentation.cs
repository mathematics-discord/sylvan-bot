namespace SylvanBot.Features.WorkingHours;

internal static class WorkingHoursPresentation
{
    public static string FormatRoles(IEnumerable<string> roles)
    {
        var text = string.Join(", ", values: roles);

        return text.Length == 0 ? "none" : text;
    }

    public static string CompletionMessage(string action, DotNext.Optional<int> pending) =>
        pending switch
        {
            { IsUndefined: true } => $"{action} pending: configuration or member data is unavailable. Reconciliation will retry.",
            { HasValue: true, Value: 0 } => $"{action} completed for the currently observed members.",
            { HasValue: true, Value: var count } => $"{action} pending for {count} member(s). Reconciliation will retry.",
        };
}