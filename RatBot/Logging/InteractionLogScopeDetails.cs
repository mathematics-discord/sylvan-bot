namespace RatBot.Logging;

public sealed record InteractionLogScopeDetails(
    string ServiceInstanceId,
    int ProcessId,
    ulong InteractionId,
    string InteractionType,
    string InteractionName,
    string InteractionCreatedAtUtc,
    ulong UserId,
    ulong? GuildId,
    ulong? ChannelId,
    string? CommandName
);