namespace RatBot.Features.Quorum.Commands;

using RatBot.Application.Common;

public static class QuorumChannelResolver
{
    public static IGuildChannel ResolveChannel(IGuild? currentGuild, IChannel channel)
    {
        if (currentGuild is null)
            throw new BotCommandException("This command can only be used in a guild.");

        if (channel is not IGuildChannel guildChannel || guildChannel.GuildId != currentGuild.Id)
            throw new BotCommandException("That channel does not belong to this guild.");

        if (channel is SocketThreadChannel thread)
        {
            if (thread.ParentChannel is null)
                throw new BotCommandException("The thread's parent channel could not be resolved.");

            channel = thread.ParentChannel;
        }
        else if (channel is IThreadChannel)
            throw new BotCommandException("The thread's parent channel could not be resolved.");

        return channel switch
        {
            IForumChannel forum => forum,
            ITextChannel text => text,
            _ => throw new BotCommandException("Choose a guild text channel or forum channel."),
        };
    }
}