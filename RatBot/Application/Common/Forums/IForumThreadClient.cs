namespace RatBot.Application.Common.Forums;

public interface IForumThreadClient
{
    Task<ErrorOr<PublishedForumThread>> CreateThreadWithMessagesAsync(ulong channelId, string title,
        IReadOnlyList<string> messages);
}