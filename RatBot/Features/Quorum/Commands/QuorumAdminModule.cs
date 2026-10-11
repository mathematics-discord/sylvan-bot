using RatBot.Application.Features.Quorum;
using RatBot.Domain.Features.Quorum;

namespace RatBot.Features.Quorum.Commands;

[Group("quorum-admin", "Administrative quorum commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class QuorumAdminModule(QuorumOperations operations) : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("register", "Register or update quorum for a channel.")]
    public async Task RegisterAsync(
        [Summary("channel", "The guild text channel, forum channel, or a thread under one.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel channel,
        [Summary("proportion", "The quorum proportion, greater than 0 and at most 1.")]
        double proportion
    )
    {
        ErrorOr<QuorumScope> scopeResult = QuorumScopeResolver.ResolveScope(Context.Guild, channel);

        if (scopeResult.IsError)
        {
            await RespondAsync(scopeResult.FirstError.Description, ephemeral: true);
            return;
        }

        ErrorOr<QuorumRegistration> result =
            await operations.RegisterAsync(scopeResult.Value, (decimal)proportion, CancellationToken.None);

        await result.SwitchFirstAsync(
            async registration =>
                await RespondAsync(QuorumCommandResponses.Registration(registration), ephemeral: true),
            async error => await RespondAsync(error.Description, ephemeral: true)
        );
    }

    [SlashCommand("remove", "Remove quorum configuration for a channel.")]
    public async Task RemoveAsync(
        [Summary("channel", "The configured guild text channel or forum channel.")]
        [ChannelTypes(ChannelType.Text, ChannelType.Forum, ChannelType.NewsThread, ChannelType.PublicThread,
            ChannelType.PrivateThread)]
        IChannel channel
    )
    {
        ErrorOr<QuorumScope> scopeResult = QuorumScopeResolver.ResolveScope(Context.Guild, channel);

        if (scopeResult.IsError)
        {
            await RespondAsync(scopeResult.FirstError.Description, ephemeral: true);
            return;
        }

        ErrorOr<Deleted> result = await operations.RemoveAsync(scopeResult.Value, CancellationToken.None);

        await result.SwitchFirstAsync(
            async _ => await RespondAsync(QuorumCommandResponses.Removed(scopeResult.Value), ephemeral: true),
            async error => await RespondAsync(error.Description, ephemeral: true)
        );
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
        bool shouldAdd = true
    )
    {
        ErrorOr<QuorumScope> scopeResult = QuorumScopeResolver.ResolveScope(Context.Guild, channel);
        ErrorOr<ulong> roleIdResult = QuorumScopeResolver.ResolveRoleId(Context.Guild, role);

        if (scopeResult.IsError || roleIdResult.IsError)
        {
            Error error = scopeResult.IsError ? scopeResult.FirstError : roleIdResult.FirstError;
            await RespondAsync(error.Description, ephemeral: true);
            return;
        }

        QuorumRoleUpdate update = QuorumRoleUpdate.FromOption(scopeResult.Value, roleIdResult.Value, shouldAdd);
        ErrorOr<QuorumConfiguration> result = await operations.UpdateRoleAsync(update, CancellationToken.None);

        await result.SwitchFirstAsync(
            async _ => await RespondAsync(QuorumCommandResponses.Role(scopeResult.Value, role, shouldAdd),
                ephemeral: true),
            async error => await RespondAsync(error.Description, ephemeral: true)
        );
    }
}