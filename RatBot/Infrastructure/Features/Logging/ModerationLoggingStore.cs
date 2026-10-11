using Microsoft.EntityFrameworkCore;
using RatBot.Domain.Features.Logging;
using RatBot.Infrastructure.Data;

namespace RatBot.Infrastructure.Features.Logging;

public sealed class ModerationLoggingStore(IDbContextFactory<BotDbContext> dbContextFactory)
{
    public async Task<int> DeleteExpiredMetadataAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);
        int deletedObservedMessages = await db
            .ObservedMessages.Where(message => message.ObservedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        int deletedLogEntries = await db
            .MessageLogEntries.Where(entry => entry.CapturedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        return deletedObservedMessages + deletedLogEntries;
    }

    public async Task<ExcludeChannelResult> ExcludeAsync(ulong guildId, ulong channelId, DateTimeOffset excludedAtUtc,
        CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        bool exists = await IsExcludedAsync(guildId, channelId, ct).ConfigureAwait(false);

        if (exists)
            return ExcludeChannelResult.AlreadyExcluded;

        db.LoggingExcludedChannels.Add(new LoggingExcludedChannel(guildId, channelId, excludedAtUtc));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ExcludeChannelResult.Excluded;
    }

    public async Task<Option<ObservedMessage>> FindObservedMessageAsync(ulong guildId, ulong originalMessageId,
        CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        ObservedMessage? observed = await db.ObservedMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                message => message.GuildId == guildId && message.OriginalMessageId == originalMessageId, ct)
            .ConfigureAwait(false);

        return observed is null ? new None() : new Some<ObservedMessage>(observed);
    }

    public async Task<IReadOnlyDictionary<ulong, ObservedMessage>> FindObservedMessagesAsync(
        ulong guildId,
        IEnumerable<ulong> originalMessageIds,
        CancellationToken ct
    )
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        ulong[] ids = originalMessageIds.Distinct().ToArray();

        return await db
            .ObservedMessages.AsNoTracking()
            .Where(message => message.GuildId == guildId && ids.Contains(message.OriginalMessageId))
            .ToDictionaryAsync(message => message.OriginalMessageId, ct)
            .ConfigureAwait(false);
    }

    public async Task<LoggingConfiguration> GetConfigurationAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        LoggingConfiguration? configuration = await db
            .LoggingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.GuildId == guildId, ct)
            .ConfigureAwait(false);

        return configuration ?? LoggingConfiguration.Disabled(guildId);
    }

    public async Task<IncludeChannelResult> IncludeAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        LoggingExcludedChannel? excludedChannel = await db
            .LoggingExcludedChannels.Where(channel => channel.GuildId == guildId && channel.ChannelId == channelId)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (excludedChannel is null)
            return IncludeChannelResult.NotExcluded;

        db.LoggingExcludedChannels.Remove(excludedChannel);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return IncludeChannelResult.Included;
    }

    public async Task<bool> IsExcludedAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db
            .LoggingExcludedChannels.AsNoTracking()
            .AnyAsync(channel => channel.GuildId == guildId && channel.ChannelId == channelId, ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsAnyExcludedAsync(ulong guildId, IEnumerable<ulong> channelIds, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);
        ulong[] ids = channelIds.Distinct().ToArray();

        if (ids.Length == 0)
            return false;

        return await db
            .LoggingExcludedChannels.AsNoTracking()
            .AnyAsync(channel => channel.GuildId == guildId && ids.Contains(channel.ChannelId), ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LoggingExcludedChannel>> ListExclusionsAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db
            .LoggingExcludedChannels.AsNoTracking()
            .Where(channel => channel.GuildId == guildId)
            .OrderBy(channel => channel.ChannelId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task ObserveMessageAsync(ObservedMessage observedMessage, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        ObservedMessage? existing = await db
            .ObservedMessages.FindAsync(new object[] { observedMessage.GuildId, observedMessage.OriginalMessageId }, ct)
            .ConfigureAwait(false);

        if (existing is null)
            db.ObservedMessages.Add(observedMessage);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordLogEntriesAsync(IEnumerable<MessageLogEntry> entries, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        MessageLogEntry[] materialized = entries.ToArray();

        if (materialized.Length == 0)
            return;

        db.MessageLogEntries.AddRange(materialized);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ErrorOr<LoggingConfiguration>> UpdateConfigurationAsync(
        ulong guildId,
        bool? enabled,
        ulong? deleteLogChannelId,
        ulong? editLogChannelId,
        TimeSpan? evidenceRetentionPeriod,
        CancellationToken ct
    )
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

        if (evidenceRetentionPeriod <= TimeSpan.Zero)
            return Error.Validation(description: "Evidence retention period must be greater than zero seconds.");

        LoggingConfiguration existing = await GetConfigurationAsync(guildId, ct).ConfigureAwait(false);

        LoggingConfiguration updated;
        try
        {
            updated = existing.WithUpdate(enabled, deleteLogChannelId, editLogChannelId, evidenceRetentionPeriod);
        }
        catch (ArgumentException)
        {
            return Error.Validation(description: "Enable logging only after setting a delete or edit log channel.");
        }

        LoggingConfiguration? tracked =
            await db.LoggingConfigurations.FindAsync(new object[] { guildId }, ct).ConfigureAwait(false);

        if (tracked is not null)
            db.LoggingConfigurations.Remove(tracked);

        db.LoggingConfigurations.Add(updated);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return updated;
    }
}