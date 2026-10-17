using RatBot.Features.Quorum.Persistence;

namespace RatBot.Features.Quorum.Commands;

[Group("quorum", "Quorum commands.")]
[DefaultMemberPermissions(GuildPermission.MuteMembers)]
public sealed class QuorumModule(QuorumConfigurationStore configurations, ILogger logger)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("calculate", "Calculate required quorum for a channel.")]
    public async Task CalculateAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel? channel = null)
    {
        ErrorOr<IGuildChannel> channelResult =
            QuorumChannelResolver.ResolveChannel(Context.Guild, channel ?? Context.Channel);

        if (channelResult.IsError)
        {
            await RespondAsync(channelResult.FirstError.Description, ephemeral: true);
            return;
        }

        // A real ephemeral acknowledgement leaves followup visibility independent of a deferred response.
        await RespondAsync("Calculating quorum…", ephemeral: true);

        IGuildChannel ch = channelResult.Value;
        ErrorOr<QuorumConfiguration> configuration =
            await configurations.GetAsync(ch.GuildId, ch.Id, CancellationToken.None);

        if (configuration.IsError)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = configuration.FirstError.Description);
            return;
        }

        ErrorOr<(int EligibleVoterCount, int RequiredVotes)> calculation = await QuorumCalculator.CalculateAsync(
            Context.Guild, configuration.Value, logger, CancellationToken.None);

        if (calculation.IsError)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = calculation.FirstError.Description);
            return;
        }

        await SendPublicResponseAsync(QuorumCommandResponses.Calculation(
            calculation.Value.EligibleVoterCount, configuration.Value.Proportion, calculation.Value.RequiredVotes));
    }

    [SlashCommand("inspect", "Inspect quorum configuration for a channel.")]
    public async Task InspectAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel? channel = null)
    {
        ErrorOr<IGuildChannel> channelResult =
            QuorumChannelResolver.ResolveChannel(Context.Guild, channel ?? Context.Channel);

        if (channelResult.IsError)
        {
            await RespondAsync(channelResult.FirstError.Description, ephemeral: true);
            return;
        }

        await RespondAsync("Loading quorum configuration…", ephemeral: true);

        IGuildChannel ch = channelResult.Value;
        ErrorOr<QuorumConfiguration> configuration =
            await configurations.GetAsync(ch.GuildId, ch.Id, CancellationToken.None);

        if (configuration.IsError)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = configuration.FirstError.Description);
            return;
        }

        await SendPublicResponseAsync(QuorumCommandResponses.Inspection(configuration.Value));
    }

    private async Task SendPublicResponseAsync(string content)
    {
        await FollowupAsync(content, allowedMentions: AllowedMentions.None);
        await DeleteOriginalResponseAsync();
    }
}