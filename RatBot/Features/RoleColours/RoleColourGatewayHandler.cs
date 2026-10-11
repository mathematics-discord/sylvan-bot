using RatBot.Gateway;

namespace RatBot.Features.RoleColours;

public sealed class RoleColourGatewayHandler(DiscordSocketClient discordClient, RoleColourReconciler colours, ILogger logger) : IDiscordGatewayHandler
{
    public Task InitializeAsync(CancellationToken ct)
    {
        discordClient.GuildMemberUpdated += HandleMemberUpdatedAsync;
        discordClient.GuildMembersDownloaded += CheckGuildAsync;
        discordClient.UserJoined += HandleMemberJoinedAsync;
        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        discordClient.GuildMemberUpdated -= HandleMemberUpdatedAsync;
        discordClient.GuildMembersDownloaded -= CheckGuildAsync;
        discordClient.UserJoined -= HandleMemberJoinedAsync;
    }

    private async Task HandleMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        IReadOnlyCollection<ulong>? previous = before.HasValue ? ((IGuildUser)before.Value).RoleIds : null;
        if (previous is not null && previous.ToHashSet().SetEquals(((IGuildUser)after).RoleIds))
            return;
        await colours.ReconcileUserRolesAsync(after.Guild, after.Id, CancellationToken.None, previous);
    }

    private Task HandleMemberJoinedAsync(SocketGuildUser member) => colours.ReconcileUserRolesAsync(member.Guild, member.Id, CancellationToken.None);

    private async Task CheckGuildAsync(SocketGuild guild)
    {
        int incomplete = await colours.ReconcileGuildAsync(guild, CancellationToken.None);
        if (incomplete > 0)
            logger.Warning("Startup role colour check incomplete for guild {GuildId}: {Incomplete} update(s)", guild.Id, incomplete);
    }
}
