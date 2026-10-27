using SylvanBot.Gateway;

namespace SylvanBot.Features.RoleColours;

public sealed class RoleColourGatewayHandler(DiscordSocketClient discordClient, RoleColourReconciler colours)
    : IDiscordGatewayHandler
{
    public Task InitializeAsync(CancellationToken ct)
    {
        discordClient.GuildMemberUpdated += HandleMemberUpdatedAsync;
        discordClient.UserJoined         += HandleMemberJoinedAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.GuildMemberUpdated -= HandleMemberUpdatedAsync;
        discordClient.UserJoined         -= HandleMemberJoinedAsync;
    }

    private async Task HandleMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        IReadOnlyCollection<ulong>? previous = before.HasValue ? ((IGuildUser)before.Value).RoleIds : null;

        if (previous is not null && previous.ToHashSet().SetEquals(((IGuildUser)after).RoleIds))
            return;

        await colours.ReconcileUserRolesAsync(guild: after.Guild, userId: after.Id, ct: CancellationToken.None, previousRoleIds: previous);
    }

    private Task HandleMemberJoinedAsync(SocketGuildUser member) => colours.ReconcileUserRolesAsync(guild: member.Guild, userId: member.Id, ct: CancellationToken.None);
}