using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.Quorum;

/// <summary>
///     Represents a contract for managing and retrieving information about the members of a quorum within a guild.
/// </summary>
public interface IQuorumMembers
{
    /// <summary>
    ///     Retrieves a list of members in a guild asynchronously. Each member includes their user ID,
    ///     whether they are a bot, and the roles assigned to them.
    /// </summary>
    /// <param name="guildId">The unique identifier of the guild from which the members are retrieved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An array of <see cref="QuorumMember"/> objects representing the guild members.</returns>
    /// <exception cref="BotCommandException">Thrown when the specified guild is not found.</exception>
    Task<QuorumMember[]> GetMembersAsync(ulong guildId, CancellationToken ct);
}