namespace SylvanBot.Features.WorkingHours;

using SylvanBot.Gateway;

public sealed class WorkingHoursGatewayHandler(DiscordSocketClient client, WorkingHoursCoordinator coordinator)
    : IDiscordGatewayHandler
{
    public Task InitializeAsync(CancellationToken ct)
    {
        client.Ready += ReadyAsync;
        client.GuildAvailable += GuildReadyAsync;
        client.GuildMembersDownloaded += GuildReadyAsync;
        client.GuildMemberUpdated += MemberUpdatedAsync;
        client.UserJoined += JoinedAsync;
        client.UserLeft += LeftAsync;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.Ready -= ReadyAsync;
        client.GuildAvailable -= GuildReadyAsync;
        client.GuildMembersDownloaded -= GuildReadyAsync;
        client.GuildMemberUpdated -= MemberUpdatedAsync;
        client.UserJoined -= JoinedAsync;
        client.UserLeft -= LeftAsync;
    }

    private Task ReadyAsync()
    {
        coordinator.DataReady();

        return Task.CompletedTask;
    }

    private Task GuildReadyAsync(SocketGuild guild)
    {
        coordinator.DataReady(guild.Id);

        return Task.CompletedTask;
    }

    private Task MemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        coordinator.MemberChanged(after);

        return Task.CompletedTask;
    }

    private Task JoinedAsync(SocketGuildUser member)
    {
        coordinator.MemberChanged(member);

        return Task.CompletedTask;
    }

    private Task LeftAsync(SocketGuild guild, SocketUser member)
    {
        coordinator.MemberLeft(guild.Id, member.Id);

        return Task.CompletedTask;
    }
}