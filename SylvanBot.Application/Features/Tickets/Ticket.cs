using static SylvanBot.Application.Features.Tickets.TicketOperationCommand;

namespace SylvanBot.Application.Features.Tickets;

using static System.StringComparison;
using System.ComponentModel.DataAnnotations.Schema;

public sealed class Ticket
{
    public const int CommentLimit = 3000;

    public long Id { get; private set; }

    public ulong GuildId { get; init; }

    public long Number { get; init; }

    public TicketAction Action { get; init; }

    public ulong SubjectUserId { get; init; }

    public ulong ModeratorUserId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string Comment { get; private set; } = string.Empty;

    public long? DurationSeconds { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset? AppliedTimeoutUntilUtc { get; private set; }

    public DateTimeOffset? TimeoutCheckAtUtc { get; set; }

    public TicketStatus Status { get; private set; }

    public bool Hidden { get; private set; }

    public bool ReviewExempt { get; init; }

    public bool ReminderExempt { get; init; }

    public ulong? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public ulong? SourceAuditId { get; init; }

    public ulong? LastAuditId { get; set; }

    public string AuditReason { get; init; } = string.Empty;

    public ulong? RoleId { get; init; }

    public long? SupersededByNumber { get; private set; }

    public bool ExpirySuspended { get; set; }

    public int Version { get; private set; }

    public TicketOperation? Pending { get; set; }

    public bool SendUncertain { get; set; }

    public bool InitialPromptPending { get; set; }

    public IList<TicketPublication> Publications { get; } = [];

    public IList<TicketHistoryEntry> ModificationHistory { get; } = [];

    [NotMapped]
    public bool Complete => !string.IsNullOrWhiteSpace(Comment)
        && (Action != TicketAction.Timeout
            || (DurationSeconds is > 0 && ExpiresAtUtc is not null));

    public static DateTimeOffset NativeTimeoutExpiry(DateTimeOffset expiry, DateTimeOffset now)
    {
        var limit = now.AddDays(28).AddMinutes(-1);
        var target = expiry < limit ? expiry : limit;

        // Discord's returned timestamps have millisecond precision; recovery compares exactly.
        return DateTimeOffset.FromUnixTimeMilliseconds(target.ToUnixTimeMilliseconds());
    }

    public static void ValidateComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment) || comment.Length > CommentLimit)
            throw new BotCommandException($"Comments must contain 1 to {CommentLimit} characters.");
    }

    public bool CanCorrect(TicketActor actor) => actor.GuildId == GuildId
        && actor.UserId != 0
        && (actor.IsAdministrator
            || actor.CanViewAuditLog
            || (Action == TicketAction.Note && actor.CanTimeoutMembers)
            || (actor.UserId == ModeratorUserId
                && Action switch
                {
                    TicketAction.Timeout => actor.CanTimeoutMembers,
                    TicketAction.Ban => actor.CanBanMembers,
                    TicketAction.VoiceMute => actor.CanMuteMembers,
                    TicketAction.VoiceDeafen => actor.CanDeafenMembers,
                    TicketAction.Kick => actor.CanKickMembers,
                    TicketAction.Role => actor.CanManageRoles,
                    _ => false,
                }));

    public void RequireEdit(TicketActor actor, int version)
    {
        if (!CanCorrect(actor))
            throw new BotCommandException("You do not have permission to edit this ticket.");

        if (Version != version)
            throw new BotCommandException("This ticket changed. Reopen it before saving.");

        if (Pending is not null)
            throw new BotCommandException("Resolve the pending Discord operation before editing details.");
    }

    public string ValidateEdit(TicketDetails details, DateTimeOffset now)
    {
        if (details.Duration.Value is null || details.Comment.Value is null)
            throw new BotCommandException("Duration and comment edit choices are required.");

        var comment = details.Comment.Apply(Comment);

        if (details.Comment.Value is not TicketCommentEdit.Keep)
            ValidateComment(comment);

        if (!details.HasDuration)
            return comment;

        if (Action is not (TicketAction.Ban or TicketAction.Timeout or TicketAction.VoiceMute or TicketAction.VoiceDeafen or TicketAction.Role))
            throw new BotCommandException("This ticket type does not accept a duration.");

        _ = Action == TicketAction.Timeout
            ? details.TimeoutExpiry(CreatedAtUtc, now)
            : details.Expiry(CreatedAtUtc);

        return comment;
    }

    public void SetComment(string comment, ulong moderatorId, bool append = false)
    {
        ValidateComment(comment);

        if (string.Equals(Comment, comment, Ordinal))
            return;

        Comment = comment;
        Record(append ? TicketHistoryAction.AppendedComment : TicketHistoryAction.SetComment, moderatorId);
    }

    public void SetDuration(long? seconds, ulong? moderatorId, DateTimeOffset? observedExpiry = null)
    {
        DateTimeOffset? expiry = observedExpiry ?? (seconds is { } finite ? CreatedAtUtc.AddSeconds(finite) : null);

        if (DurationSeconds == seconds && ExpiresAtUtc == expiry && !ExpirySuspended)
            return;

        DurationSeconds = seconds;
        ExpiresAtUtc = expiry;
        ExpirySuspended = false;
        Record(TicketHistoryAction.SetDuration, moderatorId)?.SetDuration(seconds ?? long.MaxValue);
    }

    public void SetHidden(TicketActor actor, int version, bool hidden)
    {
        var senior = actor.IsAdministrator || actor.CanViewAuditLog;

        if (!CanCorrect(actor) || (Action != TicketAction.Note && !senior) || (Hidden && !senior))
            throw new BotCommandException("You cannot change this ticket's visibility.");

        if (version != Version)
            throw new BotCommandException("This ticket changed. Refresh it before changing visibility.");

        if (Hidden == hidden)
            return;

        Hidden = hidden;
        Record(hidden ? TicketHistoryAction.Hidden : TicketHistoryAction.Unhidden, actor.UserId);
    }

    public void Approve(TicketActor actor, int version)
    {
        if (actor.GuildId != GuildId || actor.UserId == 0 || actor is { IsAdministrator: false, CanViewAuditLog: false })
            throw new BotCommandException("Approval requires Administrator or View Audit Log.");

        if (version != Version || Hidden || !Complete || Pending is not null)
            throw new BotCommandException("Complete the details, resolve pending changes and refresh before approving.");

        if (ApprovedByUserId is not null || ReviewExempt)
            return;

        ApprovedByUserId = actor.UserId;
        ApprovedAtUtc = DateTimeOffset.UtcNow;
        Record(TicketHistoryAction.Approved, actor.UserId);
    }

    public void End(TicketStatus status, ulong? moderatorId = null, long? successor = null)
    {
        if (Status == status)
            return;

        Status = status;
        SupersededByNumber = successor;

        Record(
            status switch
            {
                TicketStatus.Expired => TicketHistoryAction.Expired,
                TicketStatus.Superseded => TicketHistoryAction.Superseded,
                _ => TicketHistoryAction.Reverted,
            },
            moderatorId);
    }

    public TicketHistoryEntry? Record(TicketHistoryAction action, ulong? moderatorId = null, DateTimeOffset? timestamp = null)
    {
        Version++;
        var entry = new TicketHistoryEntry(ModificationHistory.Count + 1, action, moderatorId, timestamp ?? DateTimeOffset.UtcNow);

        if (!entry.IsModeratorChange)
            return null;

        ModificationHistory.Add(entry);

        return entry;
    }

    public void BeginTimeoutChange(TicketOperation operation)
    {
        if (Action != TicketAction.Timeout || Pending is not null || operation.Command.Value is not ChangeTimeout)
            throw new BotCommandException("This ticket cannot accept that pending change.");

        Pending = operation;
        Version++;
    }

    public bool NeedsTimeoutRenewal(DateTimeOffset now) => Action == TicketAction.Timeout
        && Status == TicketStatus.Active
        && !ExpirySuspended
        && ExpiresAtUtc > now
        && (AppliedTimeoutUntilUtc is null
            || (AppliedTimeoutUntilUtc < ExpiresAtUtc && AppliedTimeoutUntilUtc <= now.AddDays(7)));

    public void ObserveTimeoutWindow(DateTimeOffset expiry, DateTimeOffset now)
    {
        AppliedTimeoutUntilUtc = expiry;
        TimeoutCheckAtUtc = ExpiresAtUtc < now.AddDays(1) ? ExpiresAtUtc : now.AddDays(1);
    }

    public void ConfirmTimeoutChange(DateTimeOffset now)
    {
        var pending = Pending ?? throw new InvalidOperationException("No timeout change is pending.");

        if (pending.Command.Value is not ChangeTimeout change)
            throw new InvalidOperationException("The pending operation is not a timeout change.");

        SetDuration(Math.Max(1, (long)Math.Ceiling((change.ExpiryUtc - CreatedAtUtc).TotalSeconds)), pending.RequestedBy, change.ExpiryUtc);
        ObserveTimeoutWindow(pending.NativeExpiryUtc ?? change.ExpiryUtc, now);

        if (change.ExpiryUtc <= now)
            End(TicketStatus.Expired, pending.RequestedBy);

        Pending = null;
    }

    public void Initialise(string comment, long? seconds, DateTimeOffset? expiry = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(GuildId);
        ArgumentOutOfRangeException.ThrowIfZero(SubjectUserId);
        ArgumentOutOfRangeException.ThrowIfZero(ModeratorUserId);

        if (comment.Length > CommentLimit)
            throw new BotCommandException("The comment is too long.");

        InitialPromptPending = SourceAuditId is not null
            && !ReminderExempt
            && Action is TicketAction.Ban or TicketAction.Timeout;

        Comment = comment;
        DurationSeconds = seconds;
        ExpiresAtUtc = expiry ?? (seconds is { } finite ? CreatedAtUtc.AddSeconds(finite) : null);

        Status = Action switch
        {
            TicketAction.Note => TicketStatus.NotApplicable,
            TicketAction.Kick => TicketStatus.Applied,
            _ => TicketStatus.Active,
        };

        if (Action == TicketAction.Timeout && ExpiresAtUtc is { } timeoutExpiry)
            ObserveTimeoutWindow(timeoutExpiry, CreatedAtUtc);
    }
}