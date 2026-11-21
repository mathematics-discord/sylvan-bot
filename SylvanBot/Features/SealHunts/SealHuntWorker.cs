using System.Globalization;
using System.Net;
using System.Text.Json;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SylvanBot.Application.Features.SealHunts;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RoleReactions;

namespace SylvanBot.Features.SealHunts;

public sealed class SealHuntWorker(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    ILogger logger) : BackgroundService
{
    private static readonly string AssetDirectory = Path.Combine(AppContext.BaseDirectory, "Features", "SealHunts", "Assets");
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
            var now = DateTimeOffset.UtcNow;

            var needsRecovery = state.MessageId is null
                ? state.StartedAt <= now.AddMinutes(-1)
                : state.ExpiresAt is { } deadline
                    ? deadline <= now.AddSeconds(-3)
                    : state.NextAt <= now;

            if (needsRecovery)
                await RecoverPostAsync(db, id, previousChannel, state, options, ct);

            return;
        }

        if (state.NextAt > DateTimeOffset.UtcNow)
            return;

        HashSet<long> allowed = await GetDestinationsAsync(guild.Id, ct);

        IMessageChannel[] candidates = guild.Channels
            .Where(channel => IsEligible(channel, allowed))
            .OfType<IMessageChannel>()
            .ToArray();

        if (candidates.Length == 0)
            return;

        var destination = candidates[Random.Shared.Next(candidates.Length)];
        await ResolveRoleAsync(db, guild, state.RoleId, options, ct);
        await RunHuntAsync(db, guild.Id, destination, options, ct);
    }

    private static async Task<SealHunt> LoadScheduleAsync(BotDbContext db, long id, CancellationToken ct)
    {
        IQueryable<SealHunt> schedules = db.SealHunts
            .AsNoTracking()
            .Where(hunt => hunt.GuildId == id);

        var state = await schedules.SingleOrDefaultAsync(ct);

        if (state is not null)
            return state;

        var created = new SealHunt { GuildId = id, NextAt = DateTimeOffset.UtcNow + SampleInterval() };
        db.SealHunts.Add(created);

        try
        {
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(created).State = EntityState.Detached;
            return await schedules.SingleAsync(ct);
        }
    }

    private async Task ResolveRoleAsync(
        BotDbContext db,
        SocketGuild guild,
        long? savedRoleId,
        RequestOptions options,
        CancellationToken ct)
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

            role = await guild.CreateRoleAsync(
                "Sealed",
                GuildPermissions.None,
                color: null,
                isHoisted: false,
                isMentionable: false,
                options: options,
                emoji: emoji);

            await db.SealHunts
                .Where(hunt => hunt.GuildId == (long)guild.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(hunt => hunt.RoleId, (long)role.Id), ct);
        }

        var manageable = guild.CurrentUser.GuildPermissions.ManageRoles && role.Position < guild.CurrentUser.Hierarchy;

        if (!manageable)
            throw new InvalidOperationException($"Sylvan cannot grant the Sealed role {role.Id}.");
    }

    private async Task RunHuntAsync(
        BotDbContext db,
        ulong guildId,
        IMessageChannel destination,
        RequestOptions options,
        CancellationToken ct)
    {
        var id = (long)guildId;
        var started = DateTimeOffset.UtcNow;
        var key = started.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Keep the destination and button key so a restart during the upload can find the post.
        var reserved = await db.SealHunts
            .Where(hunt => hunt.GuildId == id && hunt.ChannelId == null && hunt.NextAt <= started)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(hunt => hunt.ChannelId, (long)destination.Id)
                    .SetProperty(hunt => hunt.StartedAt, started),
                ct);

        if (reserved == 0)
            return;

        var photo = _photos[Random.Shared.Next(_photos.Length)];

        var message = await destination.SendFileAsync(
            Path.Combine(AssetDirectory, photo.File),
            options: options,
            allowedMentions: AllowedMentions.None,
            components: SealHuntModule.BuildPost(photo, key),
            flags: MessageFlags.ComponentsV2);

        var next = message.Timestamp + SampleInterval();

        await db.SealHunts
            .Where(hunt => hunt.GuildId == id && hunt.StartedAt == started)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(hunt => hunt.NextAt, next)
                    .SetProperty(hunt => hunt.MessageId, (long)message.Id),
                ct);
    }

    private static async Task DeleteAtExpiryAsync(IMessage message, DateTimeOffset deadline, CancellationToken ct)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;

        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);

        try
        {
            await message.DeleteAsync(new RequestOptions { CancelToken = ct });
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            // The post was already removed.
        }
    }

    private async Task RecoverPostAsync(
        BotDbContext db,
        long id,
        long channelId,
        SealHunt state,
        RequestOptions options,
        CancellationToken ct)
    {
        try
        {
            var channel = await client.Rest.GetChannelAsync((ulong)channelId, options) as IMessageChannel;

            if (channel is not null && state.MessageId is long messageId)
            {
                if (state.ExpiresAt.HasValue)
                    await channel.DeleteMessageAsync((ulong)messageId, options);
                else if (await channel.GetMessageAsync((ulong)messageId, options: options) is not null)
                    return;
            }
            else if (channel is not null && state.StartedAt is DateTimeOffset started)
            {
                var message = await FindInterruptedPostAsync(channel, started, options);

                if (message is not null)
                {
                    var next = message.Timestamp + SampleInterval();

                    await db.SealHunts
                        .Where(hunt => hunt.GuildId == id && hunt.StartedAt == started)
                        .ExecuteUpdateAsync(
                            update => update
                                .SetProperty(hunt => hunt.NextAt, next)
                                .SetProperty(hunt => hunt.MessageId, (long)message.Id),
                            ct);

                    return;
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
        var claimId = string.Create(CultureInfo.InvariantCulture, $"seal:claim:{started.ToUnixTimeMilliseconds()}");
        var cursor = SnowflakeUtils.ToSnowflake(started.AddMinutes(-1));

        while (true)
        {
            IEnumerable<IMessage> messages = await channel
                .GetMessagesAsync(cursor, Direction.After, limit: 100, options: options)
                .FlattenAsync();

            IMessage[] page = messages
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
        db.SealHunts
            .Where(hunt => hunt.GuildId == guildId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(hunt => hunt.ChannelId, (long?)null)
                    .SetProperty(hunt => hunt.MessageId, (long?)null)
                    .SetProperty(hunt => hunt.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(hunt => hunt.ExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(hunt => hunt.Abandoned, Array.Empty<long>()),
                ct);

    private static TimeSpan SampleInterval()
    {
        var radius = Math.Sqrt(-2 * Math.Log(1 - Random.Shared.NextDouble()));
        var standardNormal = radius * Math.Cos(Math.Tau * Random.Shared.NextDouble());
        return IntervalFromStandardNormal(standardNormal);
    }

    private static TimeSpan IntervalFromStandardNormal(double standardNormal) =>
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

    private static bool IncludesDestination(HashSet<long> allowed, ulong channelId, ulong parentId, ulong? categoryId, bool publicChat) =>
        publicChat
        || allowed.Contains((long)channelId)
        || allowed.Contains((long)parentId)
        || categoryId is ulong category && allowed.Contains((long)category);

    private static bool EveryoneCanChat(SocketGuildChannel channel)
    {
        var everyone = channel.Guild.EveryoneRole;
        OverwritePermissions? overwrite = channel.GetPermissionOverwrite(everyone);
        var effective = (everyone.Permissions.RawValue & ~(overwrite?.DenyValue ?? 0)) | (overwrite?.AllowValue ?? 0);
        const ulong needed = (ulong)(ChannelPermission.ViewChannel | ChannelPermission.SendMessages);
        return everyone.Permissions.Administrator || (effective & needed) == needed;
    }

    internal async Task<HashSet<long>> GetDestinationsAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return await db.SealDestinations
            .Where(destination => destination.GuildId == (long)guildId)
            .Select(destination => destination.DestinationId)
            .ToHashSetAsync(ct);
    }

    internal async Task SetDestinationAsync(ulong guildId, ulong destinationId, bool allow, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        if (!allow)
        {
            await db.SealDestinations
                .Where(row => row.GuildId == (long)guildId && row.DestinationId == (long)destinationId)
                .ExecuteDeleteAsync(ct);

            return;
        }

        db.SealDestinations.Add(new SealDestination { GuildId = (long)guildId, DestinationId = (long)destinationId });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The destination is already allowed.
        }
    }

    internal async Task<string> ChooseAsync(
        SocketMessageComponent click,
        SocketGuildUser member,
        string key,
        bool claim,
        CancellationToken ct)
    {
        if (click.Message.Author.Id != client.CurrentUser.Id)
            return "This seal has gone.";

        var guildId = (long)member.Guild.Id;
        var userId = (long)member.Id;

        await using var db = await contexts.CreateDbContextAsync(ct);

        var state = await LoadScheduleAsync(db, guildId, ct);
        var currentKey = state.StartedAt?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var current = string.Equals(currentKey, key, StringComparison.Ordinal)
                      && state.ChannelId == (long)click.Channel.Id
                      && (state.MessageId is null || state.MessageId == (long)click.Message.Id);

        if (!current || state.RoleId is not long roleId || state.ExpiresAt <= click.CreatedAt)
            return "This seal has gone.";

        IQueryable<SealHunt> appearance = db.SealHunts.Where(hunt => hunt.GuildId == guildId && hunt.StartedAt == state.StartedAt);
        IQueryable<SealHunt> open = appearance.Where(hunt => hunt.ExpiresAt == null || hunt.ExpiresAt > click.CreatedAt);

        if (!claim)
        {
            var changed = await open.ExecuteUpdateAsync(
                update => update.SetProperty(
                    hunt => hunt.Abandoned,
                    hunt => hunt.Abandoned.Contains(userId) ? hunt.Abandoned : BotDbContext.AppendUserId(hunt.Abandoned, userId)),
                ct);

            return changed == 0 ? "This seal has gone." : _replies[Random.Shared.Next(_replies.Length)];
        }

        if (((IGuildUser)member).RoleIds.Contains((ulong)roleId))
            return "You have already claimed him. You may not do so again.";

        var role = member.Guild.GetRole((ulong)roleId);

        if (role is not null && RoleReactionPolicy.ValidateRole(member.Guild, role).HasValue)
            throw new BotCommandException("Sylvan cannot safely grant the Sealed role.");

        var lifetime = TimeSpan.FromSeconds(Math.Sqrt(2) + Random.Shared.NextDouble() * (Math.PI - Math.Sqrt(2)));
        var deadline = click.CreatedAt + lifetime;

        var opened = await appearance
            .Where(hunt => hunt.ExpiresAt == null && !hunt.Abandoned.Contains(userId))
            .ExecuteUpdateAsync(update => update.SetProperty(hunt => hunt.ExpiresAt, deadline), ct);

        if (opened == 0)
        {
            var eligible = await open.Select(hunt => (bool?)!hunt.Abandoned.Contains(userId)).SingleOrDefaultAsync(ct);

            if (eligible != true)
                return eligible is false ? "You abandoned this seal. You must live with the consequences." : "This seal has gone.";
        }

        var grant = member.AddRoleAsync((ulong)roleId, new RequestOptions { CancelToken = ct, AuditLogReason = "Claimed the seal" });

        if (opened == 1)
            await Task.WhenAll(grant, DeleteAtExpiryAsync(click.Message, deadline, ct));
        else
            await grant;

        return "You claimed him! You are now Sealed.";
    }

    [UsedImplicitly]
    internal sealed record SealPhoto(string File, string Credit, string Source, string Licence, string LicenceUrl);
}