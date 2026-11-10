namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketModerator
{
    public ulong UserId { get; init; }

    public long? ActiveTicketId { get; set; }

    public ulong? ChannelId { get; set; }

    public ulong? PromptMessageId { get; set; }

    public DateTimeOffset? NextDeliveryUtc { get; set; }

    public int Version { get; set; }

    public bool IsDeliveryDue(long ticketId, DateTimeOffset now) =>
        ActiveTicketId != ticketId || NextDeliveryUtc is null || NextDeliveryUtc <= now;

    public void ClearDelivery()
    {
        var hasDelivery = ActiveTicketId is not null || PromptMessageId is not null
                          || NextDeliveryUtc is not null;

        if (!hasDelivery)
            return;

        ActiveTicketId = null;
        PromptMessageId = null;
        NextDeliveryUtc = null;
        Version++;
    }
}
