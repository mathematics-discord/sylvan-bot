using RatBot.Application.Common.Forums;

namespace RatBot.Forum;

public sealed class ForumThreadClient(DiscordSocketClient client) : IForumThreadClient
{
    public async Task<ErrorOr<PublishedForumThread>> CreateThreadWithMessagesAsync(ulong forumChannelId, string title,
        IReadOnlyList<string> messages)
    {
        if (messages.Count == 0)
            return Error.Validation("Forum.MessagesRequired", "At least one message is required to start a thread.");

        IChannel? socketChannel = client.GetChannel(forumChannelId);

        if (socketChannel is not IForumChannel forumChannel)
            return Error.NotFound("Forum.ChannelNotFound", $"Forum channel {forumChannelId} not found.");

        if (messages.Count == 0)
            return Error.Validation("Forum.MessagesRequired", "At least one message is required to create a thread.");

        IThreadChannel thread =
            await forumChannel.CreatePostAsync(title, text: messages[0], allowedMentions: AllowedMentions.None);

        for (int i = 1; i < messages.Count; i++)
            await thread.SendMessageAsync(messages[i], allowedMentions: AllowedMentions.None);

        return new PublishedForumThread(thread.Id);
    }
}