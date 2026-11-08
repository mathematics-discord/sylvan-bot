namespace SylvanBot.Application.Features.Tickets;

using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

public sealed class TicketHistoryEntry
{
    private DateTimeOffset? _timestamp;

    private ulong? _moderatorId;

    private string? _initiatorKind;

    private long? _durationSeconds;

    internal TicketHistoryEntry(int sequence, TicketHistoryAction action, ulong? moderatorId, DateTimeOffset timestamp)
    {
        Sequence = sequence;
        Action = action;
        _timestamp = timestamp;
        _initiatorKind = moderatorId is null ? "System" : "Moderator";
        _moderatorId = moderatorId;
    }

    // EF Core materialisation.
    private TicketHistoryEntry()
    {
    }

    public int Sequence { get; private set; }

    public TicketHistoryAction Action { get; private set; }

    [NotMapped]
    public bool IsModeratorChange => Action == TicketHistoryAction.Historical
        || (_moderatorId is > 0 && Action is TicketHistoryAction.Approved
            or TicketHistoryAction.SetComment or TicketHistoryAction.AppendedComment
            or TicketHistoryAction.SetDuration or TicketHistoryAction.HideRequested
            or TicketHistoryAction.Hidden or TicketHistoryAction.Unhidden or TicketHistoryAction.StaffReverted);

    [NotMapped]
    public DateTimeOffset Timestamp => _timestamp
        ?? throw new InvalidOperationException("Historical text has no structured timestamp.");

    // Persisted history from before structured entries.
    private string? HistoricalText { get; set; }

    public string Format()
    {
        if (Action == TicketHistoryAction.Historical)
        {
            var text = HistoricalText ?? throw new InvalidOperationException("Historical entry has no text.");
            if (text.StartsWith("Legacy current record and import provenance:\n", StringComparison.Ordinal))
                return "Imported from the legacy ticket register.";

            const string marker = ", history version ";
            var versionStart = text.IndexOf(marker, StringComparison.Ordinal);
            var versionEnd = text.IndexOf(" (raw sparse row;", StringComparison.Ordinal);
            if (text.StartsWith("Legacy ticket #", StringComparison.Ordinal) && versionStart > 0
                && versionEnd > versionStart + marker.Length
                && int.TryParse(text.AsSpan(versionStart + marker.Length, versionEnd - versionStart - marker.Length),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            {
                return _timestamp is { } time
                    ? $"Legacy revision {version} · <t:{time.ToUnixTimeSeconds()}:f>"
                    : $"Legacy revision {version}";
            }

            return text;
        }

        var actor = _moderatorId is { } moderator ? $"<@{moderator}>" : "Sylvan";
        var action = Action switch
        {
            TicketHistoryAction.Approved => "approved",
            TicketHistoryAction.SetComment => "set comment",
            TicketHistoryAction.AppendedComment => "appended comment",
            TicketHistoryAction.SetDuration => $"set duration to {FormatDuration()}",
            TicketHistoryAction.HideRequested => "requested hide",
            TicketHistoryAction.Hidden => "hid",
            TicketHistoryAction.Unhidden => "unhid",
            TicketHistoryAction.Expired => "expired the action",
            TicketHistoryAction.Reverted => "reverted the action",
            TicketHistoryAction.StaffReverted => "reverted the action",
            TicketHistoryAction.Superseded => "superseded the action",
            TicketHistoryAction.AuditObserved => "recorded an audit update",
            _ => throw new InvalidOperationException("Unknown ticket history action."),
        };
        return $"- {actor} {action} at <t:{Timestamp.ToUnixTimeSeconds()}:f>";
    }

    internal void SetDuration(long seconds) => _durationSeconds = seconds;

    private string FormatDuration()
    {
        var seconds = _durationSeconds ?? throw new InvalidOperationException("Duration history has no duration.");
        return seconds == long.MaxValue
            ? "Permanent"
            : TimeSpan.FromSeconds(seconds).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}

public enum TicketHistoryAction
{
    Historical,
    Approved,
    SetDuration,
    SetComment,
    AppendedComment,
    HideRequested,
    Hidden,
    Unhidden,
    Expired,
    Reverted,
    Superseded,
    AuditObserved,
    StaffReverted,
}
