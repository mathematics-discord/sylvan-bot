// ReSharper disable ClassNeverInstantiated.Global
namespace RatBot.Features.Bridge;

using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

/// <summary>
/// Manages channel configurations for the bridge system, enabling or disabling
/// specific channels and keeping gateway filtering aligned with channel changes.
/// Provides functionality to check channel compatibility, query channels, load
/// channel configurations asynchronously, and update channel settings.
/// </summary>
public sealed class BridgeChannels : IDisposable
{
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly IDbContextFactory<BotDbContext> _contextFactory;
    private ImmutableHashSet<(ulong GuildId, ulong ChannelId)> _channels = [];

    public BridgeChannels(IDbContextFactory<BotDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public static bool IsSupported(IChannel channel)
    {
        return channel is IGuildChannel and IMessageChannel
               && channel.GetChannelType()
                   is ChannelType.Text
                   or ChannelType.News
                   or ChannelType.PublicThread
                   or ChannelType.PrivateThread
                   or ChannelType.NewsThread;
    }

    public bool Contains(ulong guildId, ulong channelId) => Volatile.Read(ref _channels).Contains((guildId, channelId));

    public ImmutableArray<ulong> List(ulong guildId)
    {
        return Volatile.Read(ref _channels)
            .Where(channel => channel.GuildId == guildId)
            .Select(channel => channel.ChannelId)
            .Order()
            .ToImmutableArray();
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken);

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            List<BridgeChannel> channels = await db.BridgeChannels
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            ImmutableHashSet<(ulong GuildId, ulong ChannelId)> snapshot = channels
                .Select(channel => (channel.GuildId, channel.ChannelId))
                .ToImmutableHashSet();

            Volatile.Write(ref _channels, snapshot);
        }
        finally
        {
            _changes.Release();
        }
    }

    public async Task<bool> SetAsync(ulong guildId, ulong channelId, bool enabled, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken);

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            int affected;

            if (enabled)
            {
                affected = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO bridge_channels (guild_id, channel_id) VALUES ({(long)guildId}, {(long)channelId}) ON CONFLICT DO NOTHING",
                    cancellationToken);
            }
            else
            {
                affected = await db.BridgeChannels
                    .Where(channel => channel.GuildId == guildId && channel.ChannelId == channelId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            ImmutableHashSet<(ulong GuildId, ulong ChannelId)> snapshot = enabled
                ? _channels.Add((guildId, channelId))
                : _channels.Remove((guildId, channelId));
            Volatile.Write(ref _channels, snapshot);

            return affected != 0;
        }
        finally
        {
            _changes.Release();
        }
    }

    public void Dispose() => _changes.Dispose();
}