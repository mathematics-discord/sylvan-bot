namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailOperation
{
    public ulong SourceMessageId { get; set; }

    public ulong SourceChannelId { get; set; }

    public ulong AuthorId { get; set; }

    public ulong GuildId { get; set; }

    public Guid? ConversationId { get; set; }

    public bool ToStaff { get; set; }

    public string State { get; set; } = "Saved";

    public string Detail { get; set; } = "Saved but unpublished";

    public ulong? DestinationChannelId { get; set; }

    public ulong? ReceiptMessageId { get; set; }

    public bool AcknowledgementStarted { get; set; }

    public ulong? AcknowledgementMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SylvanMailDelivery> Deliveries { get; set; } = new HashSet<SylvanMailDelivery>();
}

public sealed class SylvanMailDelivery
{
    public ulong SourceMessageId { get; set; }

    public int Ordinal { get; set; }

    public ulong? MessageId { get; set; }
}