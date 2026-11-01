namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketPublication
{
    public ulong ChannelId { get; init; }

    public ulong MessageId { get; init; }

    public int Revision { get; set; }

    public bool Current { get; set; }

    public bool Removed { get; set; }

    public TicketMessage Message() => new TicketMessage(ChannelId, MessageId);
}