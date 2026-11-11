namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketPublication
{
    public ulong ChannelId { get; init; }

    public ulong MessageId { get; init; }

    public TicketPublicationFormat Format { get; set; }

    public bool ApprovalReactionCleanupPending { get; set; }

    public int Revision { get; set; }

    public bool Current { get; set; }

    public bool Removed { get; set; }

    public TicketMessage Message() => new TicketMessage(ChannelId, MessageId);
}
public enum TicketPublicationFormat
{
    Unknown,
    Embed,
    ComponentsV2,
}


public sealed record TicketMessage(ulong ChannelId, ulong MessageId);
