using System.Collections.Immutable;
using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.MessageCache;

/// <summary>None leaves a field unchanged; Some supplies its current value.</summary>
public sealed record MessageEdited
{
    public required ulong GuildId { get; init; }

    public required ulong ChannelId { get; init; }

    public required ulong MessageId { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public Option<ulong> AuthorId { get; init; } = new None();

    public Option<bool> Eligible { get; init; } = new None();

    /// <summary>Gets the supplied timestamp: None preserves it; Some(null) reports "not edited".</summary>
    public Option<DateTimeOffset?> EditedAtUtc { get; init; } = new None();

    public Option<string> Content { get; init; } = new None();

    public Option<ImmutableArray<MessageAttachment>> Attachments { get; init; } = new None();
}
