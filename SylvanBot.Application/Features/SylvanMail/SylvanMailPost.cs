namespace SylvanBot.Application.Features.SylvanMail;

public sealed record SylvanMailPost
{
    public required ulong GuildId { get; init; }

    public required ulong ForumId { get; init; }

    public required long Number { get; init; }

    public required ulong UserId { get; init; }
}