using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Discord.Net;
using Discord.Rest;
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
    private readonly ConcurrentDictionary<ulong, GuildHunt> _guilds = new ConcurrentDictionary<ulong, GuildHunt>();
    private readonly SealPhoto[] _photos = ReadAsset<SealPhoto>("photos.json");
    private readonly string[] _replies = ReadAsset<string>("abandon.json");

    internal static DateTimeOffset NextHuntAt(DateTimeOffset posted, double sample)
    {
        DateTimeOffset ideal = posted.ToUniversalTime().AddHours(6);
        DateTimeOffset firstSlot = new DateTimeOffset(ideal.UtcDateTime.Date, TimeSpan.Zero).AddHours(2);

        var slotIndex = Math.Floor(((ideal - firstSlot).TotalHours + 3) / 6);
        DateTimeOffset slot = firstSlot.AddHours(6 * slotIndex);

        // calculate where the UTC slot window overlaps the allowed interval since last seal hunt
        DateTimeOffset earliest = (slot > ideal ? slot : ideal).AddHours(-2);
        DateTimeOffset latest = (slot < ideal ? slot : ideal).AddHours(2);
        return earliest.AddTicks((long)((latest - earliest).Ticks * sample));
    }

    internal static TimeSpan SampleClaimWindow(Random random)
    {
        double exponent;

        do
        {
            var radius = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble()));
            var normal = radius * Math.Cos(Math.Tau * random.NextDouble());
            exponent = 3 + (0.5 * normal);
        } while (exponent is < 2 or > 4);

        return TimeSpan.FromSeconds(Math.Pow(2, exponent));
    }

    internal async Task<HashSet<long>> GetDestinationsAsync(ulong guildId, CancellationToken ct)
    {
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);

        return await db.SealDestinations
            .Where(destination => destination.GuildId == (long)guildId)
            .Select(destination => destination.DestinationId)
            .ToHashSetAsync(ct);
    }

    internal async Task SetDestinationAsync(ulong guildId, ulong destinationId, bool allow, CancellationToken ct)
    {
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);

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
        if (click.Message.Author.Id != client.CurrentUser.Id || !_guilds.TryGetValue(member.Guild.Id, out GuildHunt? hunt))
            return "This seal has gone.";

        DateTimeOffset? firstDeadline;
        await hunt.Gate.WaitAsync(ct);

        try
        {
            ActiveSeal? seal = hunt.Active;

            if (seal?.Accepts(key, click.Message.Id, click.CreatedAt) != true)
                return "This seal has gone.";

            if (!claim)
            {
                seal.Abandoned.Add(member.Id);
                return _replies[Random.Shared.Next(_replies.Length)];
            }

            if (seal.Claimed.Contains(member.Id) || ((IGuildUser)member).RoleIds.Contains(hunt.RoleId))
                return "You have already claimed him. You may not do so again.";

            if (seal.Abandoned.Contains(member.Id))
                return "You abandoned this seal. You must live with the consequences.";

            SocketRole? role = member.Guild.GetRole(hunt.RoleId);

            if (role is not null && RoleReactionPolicy.ValidateRolePermissions(member.Guild, role).HasValue)
                throw new BotCommandException("Sylvan cannot safely grant the Sealed role.");

            await member.AddRoleAsync(hunt.RoleId, new RequestOptions { CancelToken = ct, AuditLogReason = "Claimed the seal" });
            firstDeadline = seal.RecordClaim(member.Id, DateTimeOffset.UtcNow);
        }
        finally
        {
            hunt.Gate.Release();
        }

        if (firstDeadline is not DateTimeOffset deadline)
            return "You claimed him! You are now Sealed.";

        TimeSpan remaining = deadline - DateTimeOffset.UtcNow;

        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);

        await hunt.Gate.WaitAsync(ct);

        try
        {
            if (string.Equals(hunt.Active?.Key, key, StringComparison.Ordinal))
                await RemovePostAsync(hunt, ct);
        }
        catch (HttpException ex)
        {
            logger.Warning(ex, "Could not remove expired seal {MessageId}; will retry", click.Message.Id);
        }
        finally
        {
            hunt.Gate.Release();
        }

        return "You claimed him! You are now Sealed.";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (SocketGuild guild in client.Guilds)
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

    private static T[] ReadAsset<T>(string file) =>
        JsonSerializer.Deserialize<T[]>(File.ReadAllText(Path.Combine(AssetDirectory, file)))
        ?? throw new InvalidDataException($"Missing seal hunt data in {file}.");

    private static async Task RemovePostAsync(GuildHunt hunt, CancellationToken ct)
    {
        if (hunt.Message is null)
            return;

        try
        {
            await hunt.Message.DeleteAsync(new RequestOptions { CancelToken = ct });
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            // already deleted
        }

        hunt.Message = null;
        hunt.Active = null;
    }

    private static bool IsEligible(SocketGuildChannel channel, HashSet<long> allowed)
    {
        if (channel is not IMessageChannel)
            return false;

        SocketGuildChannel parent = channel;
        SocketThreadChannel? thread = channel as SocketThreadChannel;

        if (thread is not null)
        {
            if (thread.IsArchived || thread.IsLocked)
                return false;

            parent = thread.ParentChannel;
        }

        var categoryId = (parent as INestedChannel)?.CategoryId;
        return IncludesDestination(allowed, channel.Id, parent.Id, categoryId);
    }

    internal static bool IncludesDestination(HashSet<long> allowed, ulong channelId, ulong parentId, ulong? categoryId) =>
        allowed.Contains((long)channelId)
        || allowed.Contains((long)parentId)
        || categoryId is ulong category && allowed.Contains((long)category);

    private async Task TickAsync(SocketGuild guild, CancellationToken ct)
    {
        GuildHunt hunt = _guilds.GetOrAdd(guild.Id, static _ => new GuildHunt());
        await hunt.Gate.WaitAsync(ct);

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var expired = hunt.Active?.ExpiresAt <= now;

            if (hunt.NextAt > now && !expired)
                return;

            await RemovePostAsync(hunt, ct);

            if (hunt.NextAt > now)
                return;

            HashSet<long> allowed = await GetDestinationsAsync(guild.Id, ct);

            IMessageChannel[] candidates = guild.Channels
                .Where(channel => IsEligible(channel, allowed))
                .OfType<IMessageChannel>()
                .ToArray();

            if (candidates.Length == 0)
                return;

            RequestOptions options = new RequestOptions { CancelToken = ct };
            var roleId = await ResolveRoleAsync(guild, options, ct);

            IMessageChannel destination = candidates[Random.Shared.Next(candidates.Length)];

            SealPhoto photo = _photos[Random.Shared.Next(_photos.Length)];

            var key = Guid.NewGuid().ToString("N");

            IUserMessage message = await destination.SendFileAsync(
                Path.Combine(AssetDirectory, photo.File),
                options: options,
                allowedMentions: AllowedMentions.None,
                components: SealHuntModule.BuildPost(photo, key),
                flags: MessageFlags.ComponentsV2);

            hunt.Active = new ActiveSeal(key, message.Id);
            hunt.Message = message;
            hunt.RoleId = roleId;
            hunt.NextAt = NextHuntAt(message.Timestamp, Random.Shared.NextDouble());
            logger.Debug("Next seal hunt in guild {GuildId} at {NextAt}", guild.Id, hunt.NextAt);
        }
        finally
        {
            hunt.Gate.Release();
        }
    }

    private async Task<ulong> ResolveRoleAsync(SocketGuild guild, RequestOptions options, CancellationToken ct)
    {
        await using BotDbContext db = await contexts.CreateDbContextAsync(ct);
        SealHunt? settings = await db.SealHunts.FindAsync([(long)guild.Id], ct);
        IRole? role = null;

        if (settings?.RoleId is long roleId)
        {
            role = guild.GetRole((ulong)roleId);

            if (role is null)
            {
                RestGuild restGuild = await client.Rest.GetGuildAsync(guild.Id, options);

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

        if (role is not null)
            return role.Id;

        Emoji? emoji = guild.Features.HasFeature(GuildFeature.RoleIcons) ? new Emoji("🦭") : null;

        role = await guild.CreateRoleAsync(
            "Sealed",
            GuildPermissions.None,
            color: null,
            isHoisted: false,
            isMentionable: false,
            options: options,
            emoji: emoji);

        if (settings is null)
        {
            settings = new SealHunt { GuildId = (long)guild.Id };
            db.SealHunts.Add(settings);
        }

        settings.RoleId = (long)role.Id;
        await db.SaveChangesAsync(ct);

        return role.Id;
    }

    internal sealed class ActiveSeal(string key, ulong messageId)
    {
        public string Key { get; } = key;
        public DateTimeOffset? ExpiresAt { get; private set; }

        public HashSet<ulong> Abandoned { get; } = [];
        public HashSet<ulong> Claimed { get; } = [];

        public bool Accepts(string buttonKey, ulong postId, DateTimeOffset clickedAt) => string.Equals(Key, buttonKey, StringComparison.Ordinal)
                                                                                         && messageId == postId
                                                                                         && !(ExpiresAt <= clickedAt);

        public DateTimeOffset? RecordClaim(ulong userId, DateTimeOffset now)
        {
            Claimed.Add(userId);

            if (ExpiresAt.HasValue)
                return null;

            ExpiresAt = now + SampleClaimWindow(Random.Shared);
            return ExpiresAt;
        }
    }

    [UsedImplicitly]
    internal sealed record SealPhoto(string File, string Credit, string Source, string Licence, string LicenceUrl);

    private sealed class GuildHunt
    {
        public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);
        public DateTimeOffset NextAt { get; set; }
        public ActiveSeal? Active { get; set; }
        public IUserMessage? Message { get; set; }
        public ulong RoleId { get; set; }
    }
}