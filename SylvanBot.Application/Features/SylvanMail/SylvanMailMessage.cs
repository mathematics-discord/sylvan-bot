namespace SylvanBot.Application.Features.SylvanMail;

using System.Collections.Immutable;

// Payloads live only for the lifetime of the bounded gateway work item, never in the database.
public sealed record SylvanMailMessage(
    ulong SourceMessageId,
    ulong SourceChannelId,
    ulong AuthorId,
    string InboundPrefix,
    string Username,
    ulong? GuildId,
    string Content,
    ImmutableArray<SylvanMailAttachment> Attachments,
    bool UnsupportedContent);

public sealed record SylvanMailAttachment(string Url, string Filename, int Size, bool Spoiler);
