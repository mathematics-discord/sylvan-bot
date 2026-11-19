using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RoleReactions;

namespace SylvanBot.Features.SealHunts;

public sealed class SealHuntWorker(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    ILogger logger) : BackgroundService
{
    private static readonly string AssetDirectory = Path.Combine(AppContext.BaseDirectory, "Features", "SealHunts", "Assets");
    private readonly ConcurrentDictionary<ulong, Appearance> _appearances = new ConcurrentDictionary<ulong, Appearance>();
    private readonly SealPhoto[] _photos = ReadAsset<SealPhoto>("photos.json");
    private readonly string[] _replies = ReadAsset<string>("abandon.json");

    private static T[] ReadAsset<T>(string file) =>
        JsonSerializer.Deserialize<T[]>(File.ReadAllText(Path.Combine(AssetDirectory, file)))
        ?? throw new InvalidDataException($"Missing seal hunt data in {file}.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var guild in client.Guilds)
            {
                if (!guild.IsConnected)
                    continue;

                try
                {
                    await TickAsync(guild, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.Error(ex, "Seal hunt failed in guild {GuildId}", guild.Id);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task TickAsync(SocketGuild guild, CancellationToken ct)
    {
        var id = (long)guild.Id;
        await using var db = await contexts.CreateDbContextAsync(ct);
        var state = await LoadScheduleAsync(db, id, ct);

        var options = new RequestOptions { CancelToken = ct };
        if (state.ChannelId is long previousChannel)
        {
            // Give an upload or an active seal time to finish before taking over its cleanup.
            if (state.StartedAt <= DateTimeOffset.UtcNow.AddMinutes(-1))
                await RecoverPostAsync(db, id, previousChannel, state, options, ct);
            return; // Reload the recovered schedule on the next pass.
        }

        if (state.NextAt > DateTimeOffset.UtcNow)
            return;

        var allowed = await GetDestinationsAsync(guild.Id, ct);
        var candidates = guild.Channels
            .Where(channel => IsEligible(channel, allowed))
            .OfType<IMessageChannel>()
            .ToArray();

        if (candidates.Length == 0)
            return;

        var destination = candidates[Random.Shared.Next(candidates.Length)];
        var role = await ResolveRoleAsync(db, guild, state.RoleId, options, ct);
        await RunHuntAsync(db, guild.Id, destination, role.Id, options, ct);
    }

    internal static async Task<HuntState> LoadScheduleAsync(BotDbContext db, long id, CancellationToken ct)
    {
        var schedules = db.Database
            .SqlQuery<HuntState>($"""
                SELECT next_at AS "NextAt", role_id AS "RoleId", message_id AS "MessageId", channel_id AS "ChannelId", started_at AS "StartedAt"
                FROM seal_hunts WHERE guild_id = {id}
                """);
        var state = await schedules.SingleOrDefaultAsync(ct);
        if (state is not null)
            return state;

        var first = DateTimeOffset.UtcNow + SampleInterval();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO seal_hunts (guild_id, next_at) VALUES ({id}, {first}) ON CONFLICT DO NOTHING
            """, ct);

        return await schedules.SingleAsync(ct);
    }

    private async Task<IRole> ResolveRoleAsync(BotDbContext db, SocketGuild guild, long? savedRoleId,
        RequestOptions options, CancellationToken ct)
    {
        IRole? role = null;
        if (savedRoleId is long roleId)
        {
            role = guild.GetRole((ulong)roleId);
            if (role is null)
            {
                var restGuild = await client.Rest.GetGuildAsync(guild.Id, options);
                try
                {
                    role = await restGuild.GetRoleAsync((ulong)roleId, options);
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownRole)
                {
                    // Recreate only after Discord confirms the saved role is gone.
                }
            }
        }

        if (role is null)
        {
            var emoji = guild.Features.HasFeature(GuildFeature.RoleIcons) ? new Emoji("🦭") : null;
            role = await guild.CreateRoleAsync("Sealed", GuildPermissions.None, color: null, isHoisted: false,
                isMentionable: false, options: options, emoji: emoji);

            await db.Database.ExecuteSqlAsync($"UPDATE seal_hunts SET role_id = {(long)role.Id} WHERE guild_id = {(long)guild.Id}", ct);
        }

        var manageable = guild.CurrentUser.GuildPermissions.ManageRoles && role.Position < guild.CurrentUser.Hierarchy;
        if (!manageable)
            throw new InvalidOperationException($"Sylvan cannot grant the Sealed role {role.Id}.");

        return role;
    }

    private async Task RunHuntAsync(BotDbContext db, ulong guildId, IMessageChannel destination, ulong roleId,
        RequestOptions options, CancellationToken ct)
    {
        var id = (long)guildId;
        var started = DateTimeOffset.UtcNow;
        var key = started.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Keep the destination and button key so a restart during the upload can find the post.
        var reserved = await db.Database.ExecuteSqlAsync($"""
            UPDATE seal_hunts SET channel_id = {(long)destination.Id}, started_at = {started}
            WHERE guild_id = {id} AND channel_id IS NULL AND next_at <= {started}
            """, ct);
        if (reserved == 0)
            return;

        var lifetime = TimeSpan.FromSeconds(Math.Sqrt(2) + Random.Shared.NextDouble() * (Math.PI - Math.Sqrt(2)));
        var round = new Appearance(key, roleId, lifetime);
        _appearances[guildId] = round;
        try
        {
            var photo = _photos[Random.Shared.Next(_photos.Length)];
            var message = await destination.SendFileAsync(Path.Combine(AssetDirectory, photo.File),
                components: SealHuntModule.BuildPost(photo, key), flags: MessageFlags.ComponentsV2,
                allowedMentions: AllowedMentions.None, options: options);
            var next = message.Timestamp + SampleInterval();
            var save = db.Database.ExecuteSqlAsync($"""
                UPDATE seal_hunts SET next_at = {next}, message_id = {(long)message.Id} WHERE guild_id = {id}
                """, ct);

            // Start the expiry timer immediately, even when the database is slow.
            await Task.WhenAll(save, DeleteAtExpiryAsync(message, lifetime, ct));
            await ClearMessageAsync(db, id, ct);
        }
        finally
        {
            // Discord allows three seconds for initial acknowledgement. Admit timely interactions still in transit.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            finally
            {
                _appearances.TryRemove(guildId, out _);
            }
        }
    }

    private static async Task DeleteAtExpiryAsync(IMessage message, TimeSpan lifetime, CancellationToken ct)
    {
        var remaining = message.Timestamp + lifetime - DateTimeOffset.UtcNow;
        try
        {
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, ct);
        }
        finally
        {
            try
            {
                await message.DeleteAsync();
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {
                // The post was already removed.
            }
        }
    }

    private async Task RecoverPostAsync(BotDbContext db, long id, long channelId, HuntState state,
        RequestOptions options, CancellationToken ct)
    {
        try
        {
            var channel = await client.Rest.GetChannelAsync((ulong)channelId, options) as IMessageChannel;
            if (channel is not null && state.MessageId is long messageId)
                await channel.DeleteMessageAsync((ulong)messageId, options);
            else if (channel is not null && state.StartedAt is DateTimeOffset started)
            {
                var message = await FindInterruptedPostAsync(channel, started, options);
                if (message is not null)
                {
                    var next = message.Timestamp + SampleInterval();
                    await db.Database.ExecuteSqlAsync($"""
                        UPDATE seal_hunts SET next_at = {next}, message_id = {(long)message.Id} WHERE guild_id = {id}
                        """, ct);
                    await channel.DeleteMessageAsync(message.Id, options);
                }
            }
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            // The seal or its channel was already deleted.
        }

        await ClearMessageAsync(db, id, ct);
    }

    private async Task<IMessage?> FindInterruptedPostAsync(IMessageChannel channel, DateTimeOffset started, RequestOptions options)
    {
        var claimId = FormattableString.Invariant($"seal:claim:{started.ToUnixTimeMilliseconds()}");
        var cursor = SnowflakeUtils.ToSnowflake(started.AddMinutes(-1));
        while (true)
        {
            var messages = await channel
                .GetMessagesAsync(cursor, Direction.After, limit: 100, options: options)
                .FlattenAsync();

            var page = messages
                .OrderBy(message => message.Id)
                .ToArray();

            foreach (var message in page)
            {
                if (message.Author.Id == client.CurrentUser.Id && SealHuntModule.ContainsClaim(message, claimId))
                    return message;
            }

            if (page.Length < 100 || page[^1].Timestamp > started.AddMinutes(5))
                return null;

            cursor = page[^1].Id;
        }
    }

    private static Task<int> ClearMessageAsync(BotDbContext db, long guildId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE seal_hunts SET channel_id = NULL, message_id = NULL, started_at = NULL WHERE guild_id = {guildId}
            """, ct);

    internal static TimeSpan SampleInterval()
    {
        var radius = Math.Sqrt(-2 * Math.Log(1 - Random.Shared.NextDouble()));
        var standardNormal = radius * Math.Cos(Math.Tau * Random.Shared.NextDouble());
        return IntervalFromStandardNormal(standardNormal);
    }

    internal static TimeSpan IntervalFromStandardNormal(double standardNormal) =>
        TimeSpan.FromHours(Math.Clamp(7 + Math.Sqrt(6) * standardNormal, 4, 10));

    private static bool IsEligible(SocketGuildChannel channel, HashSet<long> allowed)
    {
        if (channel is not IMessageChannel)
            return false;

        var guild = channel.Guild;
        var parent = channel;
        var thread = channel as SocketThreadChannel;
        if (thread is not null)
        {
            if (thread.IsArchived || thread.IsLocked)
                return false;

            parent = thread.ParentChannel;
        }

        var bot = guild.CurrentUser.GetPermissions(parent);
        if (thread is { IsPrivateThread: true, HasJoined: false } && !bot.ManageThreads)
            return false;

        var canSend = thread is null ? bot.SendMessages : bot.SendMessagesInThreads;
        if (!bot.ViewChannel || !bot.AttachFiles || !canSend)
            return false;

        var categoryId = (parent as INestedChannel)?.CategoryId;
        var publicChat = thread is null && EveryoneCanChat(channel);
        return IncludesDestination(allowed, channel.Id, parent.Id, categoryId, publicChat);
    }

    internal static bool IncludesDestination(HashSet<long> allowed, ulong channelId, ulong parentId, ulong? categoryId, bool publicChat) =>
        publicChat || allowed.Contains((long)channelId) || allowed.Contains((long)parentId)
        || categoryId is ulong category && allowed.Contains((long)category);

    private static bool EveryoneCanChat(SocketGuildChannel channel)
    {
        var everyone = channel.Guild.EveryoneRole;
        var overwrite = channel.GetPermissionOverwrite(everyone);
        var effective = (everyone.Permissions.RawValue & ~(overwrite?.DenyValue ?? 0)) | (overwrite?.AllowValue ?? 0);
        const ulong needed = (ulong)(ChannelPermission.ViewChannel | ChannelPermission.SendMessages);
        return everyone.Permissions.Administrator || (effective & needed) == needed;
    }

    internal async Task<HashSet<long>> GetDestinationsAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.Database
            .SqlQuery<long>($"SELECT destination_id AS \"Value\" FROM seal_destinations WHERE guild_id = {(long)guildId}")
            .ToHashSetAsync(ct);
    }

    internal async Task SetDestinationAsync(ulong guildId, ulong destinationId, bool allow, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (allow)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO seal_destinations VALUES ({(long)guildId}, {(long)destinationId}) ON CONFLICT DO NOTHING
                """, ct);
        else
            await db.Database.ExecuteSqlAsync($"""
                DELETE FROM seal_destinations WHERE guild_id = {(long)guildId} AND destination_id = {(long)destinationId}
                """, ct);
    }

    internal async Task<string> ChooseAsync(SocketMessageComponent click, SocketGuildUser member, string key, bool claim,
        CancellationToken ct)
    {
        if (!_appearances.TryGetValue(member.Guild.Id, out var round) || !string.Equals(round.Key, key, StringComparison.Ordinal) || click.Message.Author.Id != client.CurrentUser.Id)
            return "This seal has gone.";

        var decision = round.Choose(member.Id, click.CreatedAt - click.Message.Timestamp, claim,
            ((IGuildUser)member).RoleIds.Contains(round.RoleId));

        switch (decision)
        {
            case Choice.Expired: return "This seal has gone.";
            case Choice.AlreadySealed: return "You already have Sealed.";
            case Choice.AlreadyClaimed: return "You already chose to claim this seal.";
            case Choice.Abandoned: return claim ? "You abandoned this seal." : _replies[Random.Shared.Next(_replies.Length)];
        }

        try
        {
            var role = member.Guild.GetRole(round.RoleId);
            if (role is not null && RoleReactionPolicy.ValidateRole(member.Guild, role).HasValue)
                throw new BotCommandException("Sylvan cannot safely grant the Sealed role.");

            await member.AddRoleAsync(round.RoleId, new RequestOptions { CancelToken = ct, AuditLogReason = "Claimed the seal" });
            return "You claimed him! You are now Sealed.";
        }
        catch
        {
            round.ReleaseClaim(member.Id);
            throw;
        }
    }

    internal sealed record SealPhoto(string File, string Credit, string Source, string Licence, string LicenceUrl);

    internal sealed record HuntState(DateTimeOffset NextAt, long? RoleId, long? MessageId, long? ChannelId, DateTimeOffset? StartedAt);

    internal enum Choice { Claim, AlreadyClaimed, AlreadySealed, Abandoned, Expired }

    internal sealed record Appearance(string Key, ulong RoleId, TimeSpan Lifetime)
    {
        private readonly Dictionary<ulong, Choice> _choices = new Dictionary<ulong, Choice>();

        public Choice Choose(ulong userId, TimeSpan age, bool claim, bool alreadySealed)
        {
            if (age < TimeSpan.Zero || age >= Lifetime)
                return Choice.Expired;
            if (claim && alreadySealed)
                return Choice.AlreadySealed;

            lock (_choices)
            {
                if (_choices.TryGetValue(userId, out var previous))
                    return previous == Choice.Claim ? Choice.AlreadyClaimed : previous;

                var choice = claim ? Choice.Claim : Choice.Abandoned;
                _choices.Add(userId, choice);
                return choice;
            }
        }

        public void ReleaseClaim(ulong userId)
        {
            lock (_choices)
                _choices.Remove(userId);
        }
    }
}
