using Discord.Rest;

namespace SylvanBot.Features.Logging;

public sealed class ModerationAuditLookup(DiscordSocketClient discordClient, ILogger logger)
{
    private readonly ILogger _logger = logger.ForContext<ModerationAuditLookup>();

    public async Task<ulong?> TryGetBulkDeleteModeratorAsync(
        ulong guildId,
        ulong channelId,
        int originalMessageCount,
        ulong? soleAuthorId,
        DateTimeOffset deletedAt,
        CancellationToken ct)
    {
        var guild = discordClient.GetGuild(guildId);

        if (guild?.CurrentUser.GuildPermissions.ViewAuditLog != true)
            return null;

        using var lookupTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookupTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var request = new RequestOptions { CancelToken = lookupTimeout.Token };

            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken: lookupTimeout.Token);

                RestAuditLogEntry[] matchingEntries = await FindBulkDeleteEntriesAsync(
                    guild: guild,
                    channelId: channelId,
                    originalMessageCount: originalMessageCount,
                    deletedAt: deletedAt,
                    request: request);

                if (matchingEntries.Length == 1)
                    return matchingEntries[0].User?.Id;

                var canMatchBan = attempt == 2 && matchingEntries.Length == 0;

                if (!canMatchBan || soleAuthorId is not { } authorId)
                    continue;

                // Give the direct deletion entry time to arrive before considering a ban.
                return await FindBanModeratorAsync(guild: guild, authorId: authorId, deletedAt: deletedAt, request: request);
            }

            _logger.Debug(
                "No unique audit match for bulk deletion in guild {GuildId}, channel {ChannelId}",
                propertyValue0: guildId,
                propertyValue1: channelId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (lookupTimeout.IsCancellationRequested)
        {
            _logger.Debug(
                "Audit lookup timed out for bulk deletion in guild {GuildId}, channel {ChannelId}",
                propertyValue0: guildId,
                propertyValue1: channelId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                exception: ex,
                "Failed to read audit log for bulk deletion in guild {GuildId}, channel {ChannelId}",
                propertyValue0: guildId,
                propertyValue1: channelId);
        }

        return null;
    }

    private static async Task<RestAuditLogEntry[]> FindBulkDeleteEntriesAsync(
        SocketGuild guild,
        ulong channelId,
        int originalMessageCount,
        DateTimeOffset deletedAt,
        RequestOptions request)
    {
        IEnumerable<RestAuditLogEntry> bulkEntries = await guild
           .GetAuditLogsAsync(limit: 20, options: request, actionType: ActionType.MessageBulkDeleted)
           .FlattenAsync();

        return bulkEntries
           .Where(entry => Math.Abs((entry.CreatedAt - deletedAt).TotalSeconds) <= 10)
           .Where(entry =>
                      entry.Data is MessageBulkDeleteAuditLogData data && data.ChannelId == channelId && data.MessageCount == originalMessageCount)
           .ToArray();
    }

    private static async Task<ulong?> FindBanModeratorAsync(SocketGuild guild, ulong authorId, DateTimeOffset deletedAt, RequestOptions request)
    {
        IEnumerable<RestAuditLogEntry> entries = await guild
           .GetAuditLogsAsync(limit: 20, options: request, actionType: ActionType.Ban)
           .FlattenAsync();

        RestAuditLogEntry[] matchingEntries = entries
           .Where(entry => Math.Abs((entry.CreatedAt - deletedAt).TotalSeconds) <= 10)
           .Where(entry => entry.Data is BanAuditLogData data && data.Target?.Id == authorId)
           .ToArray();

        return matchingEntries.Length == 1 ? matchingEntries[0].User?.Id : null;
    }
}