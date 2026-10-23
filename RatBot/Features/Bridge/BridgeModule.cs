namespace RatBot.Features.Bridge;

[Group("bridge", "Manage Bridge channels.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireContext(ContextType.Guild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class BridgeModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BridgeChannels _channels;

    public BridgeModule(BridgeChannels channels)
    {
        _channels = channels;
    }

    [SlashCommand("add", "Enable Bridge in a channel.")]
    public Task AddAsync(
        [Summary("channel", "Channel to enable; defaults to this channel.")]
        [ChannelTypes(
            ChannelType.Text,
            ChannelType.News,
            ChannelType.PublicThread,
            ChannelType.PrivateThread,
            ChannelType.NewsThread)]
        IChannel? channel = null) =>
        SetAsync(channel ?? Context.Channel, enabled: true);

    [SlashCommand("remove", "Disable Bridge in a channel.")]
    public Task RemoveAsync(
        [Summary("channel", "Channel to disable; defaults to this channel.")]
        [ChannelTypes(
            ChannelType.Text,
            ChannelType.News,
            ChannelType.PublicThread,
            ChannelType.PrivateThread,
            ChannelType.NewsThread)]
        IChannel? channel = null) =>
        SetAsync(channel ?? Context.Channel, enabled: false);

    [SlashCommand("list", "List Bridge channels in this guild.")]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true);
        ImmutableArray<ulong> configured = _channels.List(Context.Guild.Id);

        if (configured.IsEmpty)
        {
            await ModifyOriginalResponseAsync(response =>
                response.Content = "No Bridge channels are configured in this guild.");

            return;
        }

        IEnumerable<string> lines = configured.Select(id => $"<#{id}> (`{id}`)");

        ImmutableArray<string> chunks = BridgeText.Split("Bridge channels:\n" + string.Join('\n', lines));

        await ModifyOriginalResponseAsync(response =>
        {
            response.Content = chunks[0];
            response.AllowedMentions = AllowedMentions.None;
        });

        IEnumerable<string> remaining = chunks.Skip(1);

        foreach (var chunk in remaining)
            await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private async Task SetAsync(IChannel channel, bool enabled)
    {
        var validChannel = channel is IGuildChannel guildChannel
                           && guildChannel.GuildId == Context.Guild.Id
                           && BridgeChannels.IsSupported(channel);

        if (!validChannel)
        {
            await RespondAsync("Choose a text, announcement, or thread channel in this guild.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var changed = await _channels.SetAsync(Context.Guild.Id, channel.Id, enabled, CancellationToken.None);
        var state = enabled ? "enabled" : "disabled";
        var qualifier = changed ? "now" : "already";

        await ModifyOriginalResponseAsync(response =>
        {
            response.Content = $"Bridge is {qualifier} {state} in <#{channel.Id}> (`{channel.Id}`).";
            response.AllowedMentions = AllowedMentions.None;
        });
    }
}