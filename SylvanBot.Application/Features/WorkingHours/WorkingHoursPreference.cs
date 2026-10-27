namespace SylvanBot.Application.Features.WorkingHours;

public sealed class WorkingHoursPreference
{
    public WorkingHoursPreference(ulong guildId, ulong memberId)
    {
        if (guildId == 0 || memberId == 0)
            throw new ArgumentException("Guild and member identities must be non-zero.", nameof(guildId));

        GuildId  = guildId;
        MemberId = memberId;
    }

    private WorkingHoursPreference()
    {
    }

    public ulong GuildId { get; private set; }

    public ulong MemberId { get; private set; }

    public bool Enabled { get; private set; }

    public string? StartTime { get; private set; }

    public string? EndTime { get; private set; }

    public int? OffsetMinutes { get; private set; }

    public string? ZoneId { get; private set; }

    // Decode stored fields directly; storage is not a slash-command submission.
    public WorkingHoursWindow? Window
    {
        get
        {
            if (StartTime is null && EndTime is null && !Enabled && OffsetMinutes is null && ZoneId is null)
                return null;

            (string Time, int? OffsetMinutes)? start = StartTime is null ? null : WorkingHoursTimeParser.ParseEndpoint(StartTime);
            (string Time, int? OffsetMinutes)? end   = EndTime is null ? null : WorkingHoursTimeParser.ParseEndpoint(EndTime);

            if (start is null || end is null || start.Value.OffsetMinutes.HasValue || end.Value.OffsetMinutes.HasValue)
                throw new InvalidOperationException("Invalid persisted availability window.");

            var window = new WorkingHoursWindow(start: start.Value.Time, end: end.Value.Time, offsetMinutes: OffsetMinutes, zoneId: ZoneId);

            return WorkingHoursResponses.Rejection(window: window, username: string.Empty) is not null
                ? throw new InvalidOperationException("Invalid persisted availability window.")
                : window;
        }
    }

    public void EnableSchedule(WorkingHoursWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (WorkingHoursResponses.Rejection(window: window, username: string.Empty) is not null)
            throw new ArgumentException("Schedules must last at least twenty minutes.", nameof(window));

        StartTime     = window.StartText;
        EndTime       = window.EndText;
        OffsetMinutes = window.OffsetMinutes;
        ZoneId        = window.ZoneId;
        Enabled       = true;
    }

    public void DisableSchedule() => Enabled = false;
}