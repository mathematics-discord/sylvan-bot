namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketConfiguration
{
    public ulong GuildId { get; init; }
    public bool Enabled { get; set; }
    public ulong? DestinationChannelId { get; set; }
}