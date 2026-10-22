namespace SylvanBot.Features.Tickets;

public sealed class TicketConfiguration
{
    public ulong GuildId { get; set; }
    public bool Enabled { get; set; }
    public ulong? DestinationChannelId { get; set; }
}
