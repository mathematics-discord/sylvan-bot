namespace SylvanBot.Features.Quorum.Commands;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Quorum;

[Group("quorum", "Quorum commands.")]
[DefaultMemberPermissions(GuildPermission.MuteMembers)]
public sealed class QuorumModule(QuorumOperations operations)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("calculate", "Calculate required quorum for a channel.", runMode: RunMode.Async)]
    public async Task CalculateAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel? channel = null)
    {
        var resolvedChannel =
            QuorumChannelResolver.ResolveChannel(Context.Guild, channel ?? Context.Channel);

        // A real ephemeral acknowledgement leaves followup visibility independent of a deferred response.
        await RespondAsync("Calculating quorum…", ephemeral: true);

        var calculation = await operations.CalculateAsync(
            resolvedChannel.GuildId, resolvedChannel.Id, CancellationToken.None);

        await SendPublicResponseAsync(QuorumCommandResponses.Calculation(calculation));
    }

    [SlashCommand("inspect", "Inspect quorum configuration for a channel.")]
    public async Task InspectAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel? channel = null)
    {
        var resolvedChannel =
            QuorumChannelResolver.ResolveChannel(Context.Guild, channel ?? Context.Channel);

        await RespondAsync("Loading quorum configuration…", ephemeral: true);

        Option<QuorumConfiguration> configuration = await operations.InspectAsync(
            resolvedChannel.GuildId, resolvedChannel.Id, CancellationToken.None);

        if (configuration.Value is not Some<QuorumConfiguration>(var configured))
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.ConfigurationNotFound);
            return;
        }

        await SendPublicResponseAsync(QuorumCommandResponses.Inspection(configured));
    }

    private async Task SendPublicResponseAsync(string content)
    {
        await FollowupAsync(content, allowedMentions: AllowedMentions.None);
        await DeleteOriginalResponseAsync();
    }
}