using System.Collections.Immutable;

namespace SylvanBot.Application.Features.SylvanMail;

// Payloads live only for the lifetime of the bounded gateway work item, never in the database.
public sealed record SylvanMailMessage(
    ulong SourceMessageId,
    ulong SourceChannelId,
    ulong AuthorId,
    DateTimeOffset Timestamp,
    string Username,
    ulong? GuildId,
    string Content,
    ImmutableArray<SylvanMailAttachment> Attachments,
    bool UnsupportedContent,
    string OutboundPrefix = ""
);

public sealed record SylvanMailAttachment(string Url, string Filename, int Size, bool Spoiler);
