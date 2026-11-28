namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailOperation
{
    public ulong SourceMessageId { get; set; }

    public ulong SourceChannelId { get; set; }

    public ulong AuthorId { get; set; }

    public ulong GuildId { get; set; }

    public Guid? ConversationId { get; set; }

    public bool ToStaff { get; set; }

    public SylvanMailDeliveryState State { get; private set; } = SylvanMailDeliveryState.Saved;

    public string Detail { get; private set; } = "Saved but unpublished";

    public ulong? DestinationChannelId { get; set; }

    public ulong? ReceiptMessageId { get; set; }

    public bool AcknowledgementStarted { get; set; }

    public ulong? AcknowledgementMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SylvanMailDelivery> Deliveries { get; set; } = new HashSet<SylvanMailDelivery>();

    public bool RecoverInterruptedAttempt()
    {
        switch (State)
        {
            case SylvanMailDeliveryState.Saved:
                State = SylvanMailDeliveryState.Unpublished;
                Detail = "Interrupted before delivery started. No automatic resend.";
                return true;

            case SylvanMailDeliveryState.Sending:
                State = SylvanMailDeliveryState.Uncertain;
                Detail = "Interrupted delivery: some content may have arrived. No automatic resend.";
                return true;

            default:
                return false;
        }
    }

    public SylvanMailDelivery StartSendingPart(int ordinal)
    {
        SylvanMailDelivery delivery = new SylvanMailDelivery { SourceMessageId = SourceMessageId, Ordinal = ordinal };
        Deliveries.Add(delivery);
        State = SylvanMailDeliveryState.Sending;

        return delivery;
    }

    public void MarkDelivered()
    {
        State = SylvanMailDeliveryState.Delivered;
        Detail = "Delivered: Discord accepted every part.";
    }

    public void MarkRefused(string detail)
    {
        State = SylvanMailDeliveryState.Refused;
        Detail = detail;
    }

    public void RecordDeliveryFailure(bool uncertainPost, Optional<string> refusal)
    {
        var confirmedParts = Deliveries
            .Count(item => item.MessageId.HasValue);

        var uncertainDelivery = Deliveries
            .Any(item => item.MessageId is null);

        SylvanMailDeliveryOutcome outcome = SylvanMailDeliveryOutcome.FromFailure(confirmedParts, uncertainDelivery, uncertainPost, refusal);
        State = outcome.State;
        Detail = outcome.Detail;
    }
}

public sealed class SylvanMailDelivery
{
    public ulong SourceMessageId { get; set; }

    public int Ordinal { get; set; }

    public ulong? MessageId { get; set; }
}