using System.Diagnostics.CodeAnalysis;
using SylvanBot.Gateway;

namespace SylvanBot.Features.RoleColours;

using DotNext;

[SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance")]
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
        Optional<ImmutableArray<ulong>> previous = default;

        if (before.HasValue)
        {
            ImmutableArray<ulong> roles = ((IGuildUser)before.Value).RoleIds
                .ToImmutableArray();

            var unchanged = roles
                .ToHashSet()
                .SetEquals(((IGuildUser)after).RoleIds);

            if (unchanged)
                return;

            previous = new Optional<ImmutableArray<ulong>>(roles);
        }

        await colours.ReconcileUserRolesAsync(
            guild: after.Guild,
            userId: after.Id,
            previousRoleIds: previous,
            previousMappings: default,
            ct: lifetime.ApplicationStopping);
    }

    private Task HandleMemberJoinedAsync(SocketGuildUser member) =>
        colours.ReconcileUserRolesAsync(
            guild: member.Guild,
            userId: member.Id,
            previousRoleIds: default,
            previousMappings: default,
            ct: lifetime.ApplicationStopping);
}