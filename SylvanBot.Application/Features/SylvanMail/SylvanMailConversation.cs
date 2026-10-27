namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailConversation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public long Number { get; set; }

    public ulong GuildId { get; set; }

    public ulong ForumId { get; set; }

    public ulong UserId { get; set; }

    public ulong? ThreadId { get; set; }

    public bool Retired { get; set; }

    public bool CreationStarted { get; set; }

    public bool NoticeStarted { get; set; }

    public ulong? NoticeMessageId { get; set; }
}