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
        DotNext.Optional<ImmutableArray<ulong>> previous = default;

        if (before.HasValue)
        {
            ImmutableArray<ulong> roles = ((IGuildUser)before.Value).RoleIds
               .ToImmutableArray();

            var unchanged = roles
               .ToHashSet()
               .SetEquals(((IGuildUser)after).RoleIds);

            if (unchanged)
                return;

            previous = new DotNext.Optional<ImmutableArray<ulong>>(roles);
        }

        await colours.ReconcileUserRolesAsync(
            guild: after.Guild,
            userId: after.Id,
            ct: lifetime.ApplicationStopping,
            previousRoleIds: previous,
            previousMappings: default);
    }

    private Task HandleMemberJoinedAsync(SocketGuildUser member) =>
        colours.ReconcileUserRolesAsync(
            guild: member.Guild,
            userId: member.Id,
            ct: lifetime.ApplicationStopping,
            previousRoleIds: default,
            previousMappings: default);
}