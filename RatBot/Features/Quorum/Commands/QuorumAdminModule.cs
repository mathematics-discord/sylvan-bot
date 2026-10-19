using RatBot.Features.Quorum.Persistence;

namespace RatBot.Features.Quorum.Commands;

[Group("quorum-admin", "Administrative quorum commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class QuorumAdminModule(QuorumConfigurationStore configurations)
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
        var validationMessage = QuorumChannelResolver.ValidationMessage(Context.Guild, channel);

        if (validationMessage is not null)
        {
            await RespondAsync(validationMessage, ephemeral: true);
            return;
        }

        var ch = QuorumChannelResolver.ResolveChannel(Context.Guild, channel);

        var validProportion = double.IsFinite(proportion) && proportion is > 0 and <= 1;

        if (!validProportion)
        {
            await RespondAsync("Quorum proportion must be greater than 0 and at most 1.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var proportionValue = (decimal)proportion;
        var created = await configurations.RegisterAsync(ch, proportionValue, CancellationToken.None);
        var response = $"{(created ? "Registered" : "Updated")} quorum for "
                       + $"{MentionUtils.MentionChannel(ch.Id)} "
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
        var validationMessage = QuorumChannelResolver.ValidationMessage(Context.Guild, channel);

        if (validationMessage is not null)
        {
            await RespondAsync(validationMessage, ephemeral: true);
            return;
        }

        var ch = QuorumChannelResolver.ResolveChannel(Context.Guild, channel);

        await DeferAsync(ephemeral: true);

        try
        {
            await configurations.DeleteAsync(ch.GuildId, ch.Id, CancellationToken.None);
        }
        catch (KeyNotFoundException)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.ConfigurationNotFound);
            return;
        }

        var response = $"Removed quorum configuration for {MentionUtils.MentionChannel(ch.Id)}.";

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
        var validationMessage = QuorumChannelResolver.ValidationMessage(Context.Guild, channel);

        if (validationMessage is not null)
        {
            await RespondAsync(validationMessage, ephemeral: true);
            return;
        }

        var ch = QuorumChannelResolver.ResolveChannel(Context.Guild, channel);

        if (role.Guild.Id != Context.Guild.Id)
        {
            await RespondAsync("That role does not belong to this guild.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            await configurations.UpdateRoleAsync(ch.GuildId, ch.Id, role.Id, shouldAdd, CancellationToken.None);
        }
        catch (KeyNotFoundException)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.ConfigurationNotFound);
            return;
        }

        var response = shouldAdd
            ? $"Added {role.Mention} as a voter role for {MentionUtils.MentionChannel(ch.Id)}."
            : $"Removed {role.Mention} from the voter roles for {MentionUtils.MentionChannel(ch.Id)}.";

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = response;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}