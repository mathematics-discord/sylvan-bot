using System.ComponentModel.DataAnnotations;

namespace SylvanBot.Application.Features.Tickets;

public sealed class Ticket
{
    public const int CommentLimit = 3000;

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

            field = value;
        }
    }

    public DateTimeOffset? CreatedAtUtc { get; init; }
    public TicketStatus? Status { get; set; }
    public long? DurationSeconds { get; set; }
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
    public bool DurationKnown { get; set; }
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

    public bool CanCorrect(TicketActor actor)
    {
        if (actor.GuildId != GuildId || actor.UserId == 0)
            return false;

        if (Hidden && !actor.IsAdministrator && !actor.CanViewAuditLog)
            return false;

        if (actor.IsAdministrator || actor.CanViewAuditLog)
            return true;

        return ModeratorUserId == actor.UserId && (Action switch
        {
            TicketAction.Timeout => actor.CanTimeoutMembers,
            TicketAction.Ban => actor.CanBanMembers,
            _ => false,
        });
    }

    public void UpdateCompletion()
    {
        if (Completion is null or TicketCompletion.Automated)
            return;

        var complete = !string.IsNullOrWhiteSpace(Comment)
                       && DurationKnown
                       && (Action != TicketAction.Timeout || DurationSeconds is > 0)
                       && (Action != TicketAction.Timeout || PendingOperationId is null);

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
