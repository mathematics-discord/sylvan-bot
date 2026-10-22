namespace RatBot.Features.Quorum;

using System.Net.Sockets;
using Discord.Net;
using RatBot.Application.Common;
using RatBot.Application.Features.Quorum;

/// <inheritdoc />
public sealed class DiscordQuorumMembers(IDiscordClient client) : IQuorumMembers
{
    /// <inheritdoc/>
    public async Task<QuorumMember[]> GetMembersAsync(ulong guildId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyCollection<IGuildUser> members;

        try
        {
            var options = new RequestOptions { CancelToken = ct };
            var guild = await client.GetGuildAsync(guildId, CacheMode.CacheOnly, options);
            ct.ThrowIfCancellationRequested();

            if (guild is null)
            {
                throw new BotCommandException(QuorumMessages.MemberDataUnavailable);
            }

            // AllowDownload retrieves the complete population; CacheOnly may return a partial electorate.
            members = await guild.GetUsersAsync(CacheMode.AllowDownload, options);
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpException or HttpRequestException or SocketException
                                       or IOException or TimeoutException or OperationCanceledException)
        {
            throw new BotCommandException(QuorumMessages.MemberDataUnavailable, ex);
        }

        QuorumMember[] population = members
            .Select(member => new QuorumMember(member.Id, member.IsBot, [.. member.RoleIds]))
            .ToArray();

        ct.ThrowIfCancellationRequested();

        return population;
    }
}