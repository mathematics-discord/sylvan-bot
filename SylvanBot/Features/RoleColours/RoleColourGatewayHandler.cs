namespace SylvanBot.Features.RoleColours;

using SylvanBot.Gateway;

public sealed class RoleColourGatewayHandler(
    DiscordSocketClient discordClient, RoleColourReconciler colours, ILogger logger)
    : IDiscordGatewayHandler
{
    public Task InitializeAsync(CancellationToken ct)
    {
        discordClient.GuildMemberUpdated += HandleMemberUpdatedAsync;
        discordClient.UserJoined += HandleMemberJoinedAsync;
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.GuildMemberUpdated -= HandleMemberUpdatedAsync;
        discordClient.UserJoined -= HandleMemberJoinedAsync;
    }

    private async Task HandleMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        IReadOnlyCollection<ulong>? previous = before.HasValue ? ((IGuildUser)before.Value).RoleIds : null;
        if (previous is not null && previous.ToHashSet().SetEquals(((IGuildUser)after).RoleIds))
            return;
        await ReconcileMemberAsync(after, previous);
    }

    private Task HandleMemberJoinedAsync(SocketGuildUser member)
    {
        return ReconcileMemberAsync(member, null);
    }

    private async Task ReconcileMemberAsync(SocketGuildUser member, IReadOnlyCollection<ulong>? previous)
    {
        try
        {
            await colours.ReconcileUserRolesAsync(member.Guild, member.Id, CancellationToken.None, previous);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Role colour gateway update incomplete for guild {GuildId}, member {MemberId}",
                member.Guild.Id, member.Id);
        }
    }
}