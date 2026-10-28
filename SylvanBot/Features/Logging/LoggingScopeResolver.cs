using Microsoft.EntityFrameworkCore;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.Logging;

public sealed class LoggingScopeResolver(DiscordSocketClient discordClient, IDbContextFactory<BotDbContext> dbContextFactory, ILogger logger)
{
    private readonly ILogger _logger = logger.ForContext<LoggingScopeResolver>();

    public async Task<IMessageChannel?> GetLogChannelAsync(ulong guildId, IChannel sourceChannel, bool forEdits, CancellationToken ct)
    {
        var configuration = await GetConfigurationAsync(guildId, sourceChannel, ct);
        var destinationChannelId = forEdits ? configuration?.EditLogChannelId : configuration?.DeleteLogChannelId;

        if (destinationChannelId is null)
            return null;

        var logChannel = discordClient.GetChannel(destinationChannelId.Value) as IMessageChannel;
        var belongsToGuild = logChannel is IGuildChannel guildChannel && guildChannel.GuildId == guildId;

        if (belongsToGuild)
            return logChannel;

        _logger.Warning(
            "Moderation log channel {ChannelId} is unavailable in guild {GuildId} for {EventKind} in channel {SourceChannelId}",
            destinationChannelId,
            guildId,
            forEdits ? "edit" : "delete",
            sourceChannel.Id);

        return null;
    }

    public async Task<LoggingConfiguration?> GetConfigurationAsync(ulong guildId, IChannel sourceChannel, CancellationToken ct)
    {
        List<ulong> scopeIds = [sourceChannel.Id];
        var parentChannel = sourceChannel;

        if (sourceChannel is SocketThreadChannel threadChannel)
        {
            parentChannel = threadChannel.ParentChannel;
            scopeIds.Add(parentChannel.Id);
        }

        if (parentChannel is INestedChannel { CategoryId: { } categoryId })
            scopeIds.Add(categoryId);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var configuration = await db.LoggingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.GuildId == guildId, ct);

        var enabled =
            configuration is { Enabled: true } && (configuration.EditLogChannelId is not null || configuration.DeleteLogChannelId is not null);

        if (!enabled || configuration is null)
            return null;

        var outputChannel = scopeIds.Contains(configuration.DeleteLogChannelId ?? 0) || scopeIds.Contains(configuration.EditLogChannelId ?? 0);

        var excluded = await db.LoggingExcludedChannels
            .AsNoTracking()
            .AnyAsync(channel => channel.GuildId == guildId && scopeIds.Contains(channel.ChannelId), ct);

        return outputChannel || excluded ? null : configuration;
    }
}