namespace RatBot.Features.Quorum;

public sealed class QuorumConfiguration
{
    public Guid Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public string ChannelKind { get; set; } = null!;
    public decimal Proportion { get; set; }
    public ImmutableHashSet<ulong> RoleIds { get; set; } = [];
}