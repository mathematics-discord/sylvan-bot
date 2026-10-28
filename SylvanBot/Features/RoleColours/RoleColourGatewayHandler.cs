using SylvanBot.Gateway;

namespace SylvanBot.Features.RoleColours;

public sealed class RoleColourGatewayHandler(DiscordSocketClient discordClient, RoleColourReconciler colours, IHostApplicationLifetime lifetime)
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
        Option<ImmutableArray<ulong>> previous = new None();

        if (before.HasValue)
        {
            ImmutableArray<ulong> roles = ((IGuildUser)before.Value).RoleIds
                .ToImmutableArray();

            var unchanged = roles
                .ToHashSet().SetEquals(((IGuildUser)after).RoleIds);

            if (unchanged)
                return;

            previous = new Some<ImmutableArray<ulong>>(roles);
        }

        await colours.ReconcileUserRolesAsync(after.Guild, after.Id, lifetime.ApplicationStopping, previous, new None());
    }

    private Task HandleMemberJoinedAsync(SocketGuildUser member) =>
        colours.ReconcileUserRolesAsync(
            guild: member.Guild,
            userId: member.Id,
            ct: lifetime.ApplicationStopping,
            previousRoleIds: new None(),
            previousMappings: new None());
}