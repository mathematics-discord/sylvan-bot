using System.Collections.Immutable;

namespace SylvanBot.Application.Features.MessageCache;

public sealed record MessageReceived
{
    public required ulong GuildId { get; init; }

    public required ulong ChannelId { get; init; }

    public required ulong MessageId { get; init; }

    public required ulong AuthorId { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset? EditedAtUtc { get; init; }

    public required bool Eligible { get; init; }

    public required string Content { get; init; }

    public required ImmutableArray<MessageAttachment> Attachments { get; init; }
}