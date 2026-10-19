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
        var validationMessage = QuorumChannelResolver.ValidationMessage(Context.Guild, channel ?? Context.Channel);

        if (validationMessage is not null)
        {
            await RespondAsync(validationMessage, ephemeral: true);
            return;
        }

        var ch = QuorumChannelResolver.ResolveChannel(Context.Guild, channel ?? Context.Channel);

        // A real ephemeral acknowledgement leaves followup visibility independent of a deferred response.
        await RespondAsync("Calculating quorum…", ephemeral: true);

        QuorumConfiguration config;
        try
        {
            config = await configurations.GetAsync(ch.GuildId, ch.Id, CancellationToken.None);
        }
        catch (KeyNotFoundException)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.ConfigurationNotFound);
            return;
        }

        if (config.RoleIds.IsEmpty)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.ConfigurationIncomplete);
            return;
        }

        (int EligibleVoterCount, int RequiredVotes) counts;
        try
        {
            counts = await QuorumCalculator.CalculateAsync(Context.Guild, config, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to retrieve quorum member data in guild {GuildId}, channel {ChannelId}",
                ch.GuildId, ch.Id);

            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.MemberDataUnavailable);
            return;
        }

        if (counts.EligibleVoterCount == 0)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.NoEligibleVoters);
            return;
        }

        await SendPublicResponseAsync(QuorumCommandResponses.Calculation(
            counts.EligibleVoterCount, config.Proportion, counts.RequiredVotes));
    }

    [SlashCommand("inspect", "Inspect quorum configuration for a channel.")]
    public async Task InspectAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel? channel = null)
    {
        var validationMessage = QuorumChannelResolver.ValidationMessage(Context.Guild, channel ?? Context.Channel);

        if (validationMessage is not null)
        {
            await RespondAsync(validationMessage, ephemeral: true);
            return;
        }

        var ch = QuorumChannelResolver.ResolveChannel(Context.Guild, channel ?? Context.Channel);

        await RespondAsync("Loading quorum configuration…", ephemeral: true);

        QuorumConfiguration config;
        try
        {
            config = await configurations.GetAsync(ch.GuildId, ch.Id, CancellationToken.None);
        }
        catch (KeyNotFoundException)
        {
            await ModifyOriginalResponseAsync(properties => properties.Content = QuorumMessages.ConfigurationNotFound);
            return;
        }

        await SendPublicResponseAsync(QuorumCommandResponses.Inspection(config));
    }

    private async Task SendPublicResponseAsync(string content)
    {
        await FollowupAsync(content, allowedMentions: AllowedMentions.None);
        await DeleteOriginalResponseAsync();
    }
}