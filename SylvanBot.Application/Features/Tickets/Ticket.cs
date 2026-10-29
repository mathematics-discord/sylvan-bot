using System.ComponentModel.DataAnnotations;

namespace SylvanBot.Application.Features.Tickets;

public sealed class Ticket
{
    public const int CommentLimit = 3000;

    public IList<string> ModificationHistory { get; set; } = [];

    public long Id { get; init; }

    public ulong GuildId { get; init; }

    public long Number { get; init; }

    public TicketAction Action { get; init; }

    public ulong? SubjectUserId { get; init; }

    public ulong? ModeratorUserId { get; init; }

    [StringLength(CommentLimit)]
    public string? Comment
    {
        get;
        set
        {
            if (value?.Length > CommentLimit)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Ticket comments cannot exceed {CommentLimit:N0} characters.");

            if (!string.Equals(field, value, StringComparison.Ordinal))
                ClearApproval();

            field = value;
        }
    }

    public DateTimeOffset? CreatedAtUtc { get; init; }

    public TicketStatus? Status { get; set; }

    public long? DurationSeconds
    {
        get;
        set
        {
            if (field != value)
                ClearApproval();

            field = value;
        }
    }

    public ulong? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public bool ReviewExempt { get; set; }

    public bool HidePending { get; set; }

    public ulong? RoleId { get; init; }

    public bool Hidden { get; set; }

    public ulong? ChannelId { get; set; }

    public ulong? MessageId { get; set; }

    public ulong? PreviousChannelId { get; set; }

    public ulong? PreviousMessageId { get; set; }

    public bool PublicationPending { get; set; }

    public ulong? LastModifiedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    public int Version { get; set; }

    public ulong? SourceAuditId { get; init; }

    public ulong? LastAuditId { get; set; }

    public string? AuditReason { get; init; }

    public bool DurationKnown
    {
        get;
        set
        {
            if (field != value)
                ClearApproval();

            field = value;
        }
    }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public TicketCompletion? Completion { get; set; }

    public long? SupersededByNumber { get; set; }

    public int? PublishedVersion { get; set; }

    public Guid? PendingOperationId { get; set; }

    public DateTimeOffset? PendingExpiryUtc { get; set; }

    public ulong? PendingEditorUserId { get; set; }

    public DateTimeOffset? PendingRequestedAtUtc { get; set; }

    public bool PendingOperationAttempted { get; set; }

    public bool ExpirySuspended { get; set; }

    public void RecordModification(ulong moderatorId, string action, DateTimeOffset timestamp)
    {
        ModificationHistory.Add($"- <@{moderatorId}> {action} at <t:{timestamp.ToUnixTimeSeconds()}:f>");
    }

    public void SetComment(string comment, ulong moderatorId, bool append = false)
    {
        var updated = append && !string.IsNullOrEmpty(Comment) ? Comment + "\n" + comment : comment;

        if (string.Equals(Comment, updated, StringComparison.Ordinal))
            return;

        Comment = updated;
        RecordModification(moderatorId, append ? "appended comment" : "set comment", DateTimeOffset.UtcNow);
    }

    public void RecordDurationChange(ulong moderatorId)
    {
        var duration = DurationSeconds is null
            ? "Permanent"
            : TimeSpan.FromSeconds(DurationSeconds.Value).ToString(@"d\D\Thh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        RecordModification(moderatorId, $"set duration to {duration}", DateTimeOffset.UtcNow);
    }

    public void ClearApproval()
    {
        ApprovedByUserId = null;
        ApprovedAtUtc = null;
    }

    public void Approve(TicketActor actor, int expectedVersion)
    {
        if (actor.GuildId != GuildId || actor.UserId == 0 || (actor is { IsAdministrator: false, CanViewAuditLog: false }))
            throw new Common.BotCommandException("Approval requires Administrator or View Audit Log.");

        if (Version != expectedVersion)
            throw new Common.BotCommandException("This ticket changed. Refresh it before approving.");

        if (Action is not (TicketAction.Ban or TicketAction.Timeout or TicketAction.VoiceMute)
            || ReviewExempt || Hidden || HidePending || PendingOperationId is not null
            || string.IsNullOrWhiteSpace(Comment) || !DurationKnown
            || (Action == TicketAction.Timeout && DurationSeconds is not > 0)
            || Completion == TicketCompletion.Pending)
            throw new Common.BotCommandException("Complete the ticket details and resolve pending changes before approving.");

        if (ApprovedAtUtc is not null)
            return;

        ApprovedByUserId = actor.UserId;
        ApprovedAtUtc = DateTimeOffset.UtcNow;
        RecordModification(actor.UserId, "approved", ApprovedAtUtc.Value);
        Version++;
    }

    public bool CanCorrect(TicketActor actor)
    {
        if (actor.GuildId != GuildId || actor.UserId == 0)
            return false;

        if (Hidden && actor is { IsAdministrator: false, CanViewAuditLog: false })
            return false;

        if (actor.IsAdministrator || actor.CanViewAuditLog)
            return true;

        return ModeratorUserId == actor.UserId && Action switch
        {
            TicketAction.Timeout => actor.CanTimeoutMembers,
            TicketAction.VoiceMute => actor.CanMuteMembers,
            TicketAction.Ban => actor.CanBanMembers,
            _ => false,
        };
    }

    public void UpdateCompletion()
    {
        if (Completion is null or TicketCompletion.Automated)
            return;

        var complete = !string.IsNullOrWhiteSpace(Comment)
                       && DurationKnown
                       && (Action != TicketAction.Timeout || DurationSeconds is > 0);

        Completion = complete ? TicketCompletion.Complete : TicketCompletion.Pending;
    }

    public void PrepareOperation(DateTimeOffset? expiry, ulong? editorUserId)
    {
        PendingOperationId = Guid.NewGuid();
        PendingExpiryUtc = expiry;
        PendingEditorUserId = editorUserId;
        PendingRequestedAtUtc = DateTimeOffset.UtcNow;
        PendingOperationAttempted = false;
        Version++;
        UpdateCompletion();
    }

    public void RejectOperation()
    {
        ClearOperation();

        if (Action == TicketAction.Ban)
            Status = TicketStatus.ExpirationFailed;

        Version++;
        UpdateCompletion();
    }

    public void SetObservedExpiry(DateTimeOffset expiry)
    {
        ExpiresAtUtc = expiry;
        DurationSeconds = Math.Max(1, (long)Math.Ceiling((expiry - CreatedAtUtc!.Value).TotalSeconds));
        DurationKnown = true;
    }

    public void ClearOperation()
    {
        PendingOperationId = null;
        PendingExpiryUtc = null;
        PendingEditorUserId = null;
        PendingRequestedAtUtc = null;
        PendingOperationAttempted = false;
    }

    public void CancelOlderOperation(TicketObservation observation)
    {
        if (LastAuditId is null || observation.AuditId > LastAuditId)
            ClearOperation();
    }

    public void ConfirmOperation(DateTimeOffset? expiry)
    {
        var editor = PendingEditorUserId;
        DateTimeOffset? requested = PendingRequestedAtUtc;

        if (Action == TicketAction.Timeout && PendingExpiryUtc is not null)
            SetObservedExpiry(PendingExpiryUtc.Value);

        if (Action == TicketAction.Timeout && expiry is not null)
            SetObservedExpiry(expiry.Value);
        else
            Status = TicketStatus.Expired;

        ClearOperation();

        if (LastModifiedAtUtc is null || LastModifiedAtUtc <= requested)
        {
            LastModifiedByUserId = editor;
            LastModifiedAtUtc = requested;
        }

        Version++;
        UpdateCompletion();
    }
}
