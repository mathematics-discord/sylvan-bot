namespace RatBot.Features.Quorum.Commands;

public static class QuorumChannelResolver
{
    public static IGuildChannel ResolveChannel(IGuild? currentGuild, IChannel ch)
    {
        var validationMessage = ValidationMessage(currentGuild, ch);

        if (validationMessage is not null)
            throw new ArgumentException(validationMessage, nameof(ch));

        return ch is SocketThreadChannel thread ? thread.ParentChannel : (IGuildChannel)ch;
    }

    internal static string? ValidationMessage(IGuild? currentGuild, IChannel ch)
    {
        if (currentGuild is null)
            return "This command can only be used in a guild.";

        if (ch is not IGuildChannel guildChannel || guildChannel.GuildId != currentGuild.Id)
            return "That channel does not belong to this guild.";

        if (ch is SocketThreadChannel thread)
            ch = thread.ParentChannel;
        else if (ch is IThreadChannel)
            return "The thread's parent channel could not be resolved.";

        return ch is IForumChannel or ITextChannel ? null : "Choose a guild text channel or forum channel.";
    }
}