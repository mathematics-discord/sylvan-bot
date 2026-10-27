namespace SylvanBot.Application.Features.Quorum;

/// <summary>
/// </summary>
public sealed class QuorumConfiguration
{
    /// <summary>
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// </summary>
    public ulong GuildId { get; init; }

    /// <summary>
    /// </summary>
    public ulong ChannelId { get; init; }

    /// <summary>
    /// </summary>
    public string ChannelKind { get; init; } = string.Empty;

    /// <summary>
    /// </summary>
    public decimal Proportion { get; init; }

    /// <summary>
    /// </summary>
    public List<QuorumVoterRole> VoterRoles { get; init; } = [];
}