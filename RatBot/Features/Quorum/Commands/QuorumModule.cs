using RatBot.Features.Quorum;

namespace RatBot.Features.Quorum.Commands;

[Group("quorum", "Quorum commands.")]
[DefaultMemberPermissions(GuildPermission.MuteMembers)]
public sealed class QuorumModule(QuorumOperations operations) : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("calculate", "Calculate required quorum for a channel.")]
    public async Task CalculateAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel? channel = null
    )
    {
        channel ??= Context.Channel;

        ErrorOr<QuorumScope> scopeResult = QuorumScopeResolver.ResolveScope(Context.Guild, channel);

        if (scopeResult.IsError)
        {
            await RespondAsync(scopeResult.FirstError.Description, ephemeral: true);
            return;
        }

        ErrorOr<QuorumCalculation> result = await operations.CalculateAsync(scopeResult.Value, CancellationToken.None);

        await result.SwitchFirstAsync(
            async calculation => await RespondAsync(QuorumCommandResponses.Calculation(calculation)),
            async error => await RespondAsync(error.Description, ephemeral: true)
        );
    }

    [SlashCommand("inspect", "Inspect quorum configuration for a channel.")]
    public async Task InspectAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel? channel = null
    )
    {
        channel ??= Context.Channel;

        ErrorOr<QuorumScope> scopeResult = QuorumScopeResolver.ResolveScope(Context.Guild, channel);

        if (scopeResult.IsError)
        {
            await RespondAsync(scopeResult.FirstError.Description, ephemeral: true);
            return;
        }

        ErrorOr<QuorumConfiguration> result = await operations.InspectAsync(scopeResult.Value, CancellationToken.None);

        await result.SwitchFirstAsync(
            async configuration => await RespondAsync(QuorumCommandResponses.Inspection(configuration)),
            async error => await RespondAsync(error.Description, ephemeral: true)
        );
    }
}