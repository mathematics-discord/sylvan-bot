namespace SylvanBot.Features.WorkingHours;

internal static class WorkingHoursPresentation
{
    public static string FormatRoles(IEnumerable<string> roles)
    {
        var text = string.Join(", ", values: roles);

        return text.Length == 0 ? "none" : text;
    }

    public static string CompletionMessage(string action, Option<int> pending) =>
        pending.Value switch
        {
            None => $"{action} pending: configuration or member data is unavailable. Reconciliation will retry.",
            Some<int>(0) => $"{action} completed for the currently observed members.",
            Some<int>(var count) => $"{action} pending for {count} member(s). Reconciliation will retry.",
            _ => throw new InvalidOperationException("Unknown reconciliation outcome."),
        };
}