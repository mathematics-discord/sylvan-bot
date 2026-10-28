using System.Collections.Immutable;
using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.MessageCache;

/// <summary>Undefined leaves a field unchanged; a defined optional supplies its current value.</summary>
public sealed record MessageEdited
{
    public required ulong GuildId { get; init; }

    public required ulong ChannelId { get; init; }

    public required ulong MessageId { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public Optional<ulong> AuthorId { get; init; } = default;

    public Optional<bool> Eligible { get; init; } = default;

    /// <summary>Gets the supplied timestamp: undefined preserves it; explicit null reports "not edited".</summary>
    public Optional<DateTimeOffset?> EditedAtUtc { get; init; } = default;

    public Optional<string> Content { get; init; } = default;

    public Optional<ImmutableArray<MessageAttachment>> Attachments { get; init; } = default;
}
