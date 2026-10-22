namespace RatBot.Features.Quorum.Commands;

using RatBot.Application.Features.Quorum;

[Group("quorum-admin", "Administrative quorum commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class QuorumAdminModule(QuorumOperations operations)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("register", "Register or update quorum for a channel.")]
    public async Task RegisterAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel channel,
        [Summary("proportion", "The quorum proportion, greater than 0 and at most 1.")]
        double proportion)
    {
        var resolvedChannel = QuorumChannelResolver.ResolveChannel(Context.Guild, channel);

        var validProportion = double.IsFinite(proportion) && proportion is > 0 and <= 1;

        if (!validProportion)
        {
            await RespondAsync(QuorumMessages.InvalidProportion, ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var proportionValue = (decimal)proportion;
        var channelKind = resolvedChannel is IForumChannel
            ? "forum"
            : "text";

        var created = await operations.RegisterAsync(
            resolvedChannel.GuildId, resolvedChannel.Id, channelKind, proportionValue, CancellationToken.None);

        var response = $"{(created ? "Registered" : "Updated")} quorum for "
                       + $"{MentionUtils.MentionChannel(resolvedChannel.Id)} "
                       + $"at {QuorumCommandResponses.FormatProportion(proportionValue)}.";

        await ModifyOriginalResponseAsync(properties => properties.Content = response);
    }

    [SlashCommand("remove", "Remove quorum configuration for a channel.")]
    public async Task RemoveAsync(
        [Summary("channel", "The configured guild text channel or forum channel.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel channel)
    {
        var resolvedChannel = QuorumChannelResolver.ResolveChannel(Context.Guild, channel);

        await DeferAsync(ephemeral: true);

        var removed = await operations.RemoveAsync(
            resolvedChannel.GuildId, resolvedChannel.Id, CancellationToken.None);

        var response = removed
            ? $"Removed quorum configuration for {MentionUtils.MentionChannel(resolvedChannel.Id)}."
            : QuorumMessages.ConfigurationNotFound;

        await ModifyOriginalResponseAsync(properties => properties.Content = response);
    }

    [SlashCommand("role", "Add or remove a quorum voter role.")]
    public async Task RoleAsync(
        [Summary("channel", "The configured guild text channel or forum channel.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel channel,
        [Summary("role", "The voter role to add or remove.")]
        IRole role,
        [Summary("should_add", "True to add the role; false to remove it.")]
        bool shouldAdd = true)
    {
        var resolvedChannel = QuorumChannelResolver.ResolveChannel(Context.Guild, channel);

        if (role.Guild.Id != Context.Guild.Id)
        {
            await RespondAsync("That role does not belong to this guild.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        await operations.SetVoterRoleAsync(
            resolvedChannel.GuildId, resolvedChannel.Id, role.Id, shouldAdd, CancellationToken.None);

        string response;

        if (shouldAdd)
            response = $"Added {role.Mention} as a voter role for {MentionUtils.MentionChannel(resolvedChannel.Id)}.";
        else
            response = $"Removed {role.Mention} from the voter roles for {MentionUtils.MentionChannel(resolvedChannel.Id)}.";

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = response;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}