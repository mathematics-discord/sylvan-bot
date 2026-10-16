using RatBot.Features.Quorum;

namespace RatBot.Features.Quorum.Commands;

public static class QuorumScopeResolver
{
    public static ErrorOr<ulong> ResolveRoleId(IGuild? currentGuild, IRole role)
    {
        if (currentGuild is null)
            return Error.Validation(description: "This command can only be used in a guild.");

        return role.Guild.Id == currentGuild.Id
            ? role.Id
            : Error.Validation(description: "That role does not belong to this guild.");
    }

    public static ErrorOr<QuorumScope> ResolveScope(IGuild? currentGuild, IChannel channel)
    {
        if (currentGuild is null)
            return Error.Validation(description: "This command can only be used in a guild.");

        if (channel is not IGuildChannel guildChannel || guildChannel.GuildId != currentGuild.Id)
            return Error.Validation(description: "That channel does not belong to this guild.");

        return channel switch
        {
            SocketThreadChannel thread => ResolveScopeChannel(currentGuild, thread.ParentChannel),
            IThreadChannel => Error.Validation(description: "The thread's parent channel could not be resolved."),
            _ => ResolveScopeChannel(currentGuild, channel)
        };
    }

    internal static ErrorOr<QuorumScope> ResolveScopeChannel(IGuild currentGuild, IChannel scopeChannel)
    {
        return scopeChannel switch
        {
            IForumChannel => new QuorumScope.ForumChannel(currentGuild.Id, scopeChannel.Id),
            ITextChannel and not IThreadChannel => new QuorumScope.TextChannel(currentGuild.Id, scopeChannel.Id),
            _ => Error.Validation(description: "Choose a guild text channel or forum channel.")
        };
    }
}