namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketModerator
{
    public ulong UserId { get; init; }
    public long? ActiveTicketId { get; set; }
    public ulong? ChannelId { get; set; }
    public ulong? PromptMessageId { get; set; }
    public int? PromptVersion { get; set; }
    public int? LastAttemptedVersion { get; set; }
    public DateTimeOffset? NextDeliveryUtc { get; set; }
    public ulong? LastReadMessageId { get; set; }
    public int Version { get; set; }

    public void ClearDelivery()
    {
        var hasDelivery = ActiveTicketId is not null || PromptMessageId is not null || PromptVersion is not null
                          || LastAttemptedVersion is not null || NextDeliveryUtc is not null;

        if (!hasDelivery)
            return;

        ActiveTicketId = null;
        PromptMessageId = null;
        PromptVersion = null;
        LastAttemptedVersion = null;
        NextDeliveryUtc = null;
        Version++;
    }
}
