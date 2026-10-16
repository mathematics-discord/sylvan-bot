namespace RatBot.Features.Quorum.Commands;

public static class QuorumChannelResolver
{
    public static ErrorOr<IGuildChannel> ResolveChannel(IGuild? currentGuild, IChannel ch)
    {
        if (currentGuild is null)
            return Error.Validation(description: "This command can only be used in a guild.");

        if (ch is not IGuildChannel guildChannel || guildChannel.GuildId != currentGuild.Id)
            return Error.Validation(description: "That channel does not belong to this guild.");

        if (ch is SocketThreadChannel thread)
            ch = thread.ParentChannel;
        else if (ch is IThreadChannel)
            return Error.Validation(description: "The thread's parent channel could not be resolved.");

        return ch switch
        {
            IForumChannel forum => ErrorOrFactory.From<IGuildChannel>(forum),
            ITextChannel text => ErrorOrFactory.From<IGuildChannel>(text),
            _ => Error.Validation(description: "Choose a guild text channel or forum channel."),
        };
    }
}