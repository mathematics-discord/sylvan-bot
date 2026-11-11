using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Activity;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RoleReactions;

namespace SylvanBot.Features.Activity;

public sealed class ActivityService(IDbContextFactory<BotDbContext> contexts)
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new ConcurrentDictionary<ulong, SemaphoreSlim>();

    public async Task RunAsync(ulong guildId, Func<BotDbContext, Task> action, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            await action(db);
        }
        finally
        {
            gate.Release();
        }
    }

    public static decimal Award(decimal weight, decimal recent, long cap) => cap == 0 ? weight : Math.Min(weight, Math.Max(0, cap - recent));

    public async Task ObserveAsync(SocketUserMessage message, CancellationToken ct)
    {
        if (message.Channel is not SocketGuildChannel channel)
            return;

        await RunAsync(
            channel.Guild.Id,
            async db =>
            {
                var settings = await db.ActivitySettings.FindAsync([channel.Guild.Id], ct);

                if (settings is null || settings.Role1Id == 0)
                    return;

                var member = await db.ActivityMembers.FindAsync([channel.Guild.Id, message.Author.Id], ct);

                if (member is { OptedOut: true })
                    return;

                member ??= new ActivityMember { GuildId = channel.Guild.Id, UserId = message.Author.Id, ResetAt = DateTimeOffset.MinValue };

                if (message.Timestamp <= member.ResetAt)
                    return;

                var duplicate = await db.ActivityAwards.AnyAsync(row => row.GuildId == channel.Guild.Id && row.MessageId == message.Id, ct);

                if (duplicate)
                    return;

                var parent = channel is SocketThreadChannel thread ? thread.ParentChannel : channel;
                var categoryId = parent is SocketForumChannel forum ? forum.CategoryId : (parent as SocketTextChannel)?.CategoryId;

                Dictionary<ulong, decimal> weights = await db.ActivityWeights
                    .Where(row => row.GuildId == channel.Guild.Id)
                    .ToDictionaryAsync(row => row.ChannelId, row => row.Weight, ct);

                var weight = weights.GetValueOrDefault(parent.Id, categoryId.HasValue ? weights.GetValueOrDefault(categoryId.Value, 1m) : 1m);
                var now = DateTimeOffset.UtcNow;
                var cutoff = now.AddHours(-24);

                var recent = await db.ActivityAwards
                    .Where(row => row.GuildId == channel.Guild.Id && row.UserId == member.UserId && row.AwardedAt > cutoff)
                    .SumAsync(row => row.Xp, ct);

                var xp = Award(weight, recent, settings.MaxDaily);

                if (db.Entry(member).State == EntityState.Detached)
                    db.ActivityMembers.Add(member);

                if (xp > 0)
                {
                    member.Xp += xp;

                    RecordAward(db, member, message.Id, now, xp);
                }

                await db.SaveChangesAsync(ct);
                await ReconcileAsync(channel.Guild, member, settings, ct);
                await db.SaveChangesAsync(ct);
            },
            ct);
    }

    public static async Task ReconcileAsync(SocketGuild guild, ActivityMember member, ActivitySettings settings, CancellationToken ct)
    {
        var user = await ((IGuild)guild).GetUserAsync(member.UserId, options: new RequestOptions { CancelToken = ct });

        if (user is null)
            return;

        ulong desired = 0;

        if (!member.OptedOut && settings.Role1Id != 0)
            if (member.Xp >= settings.Role1Score)
                desired = settings.Role1Id;
            else if (member.Xp >= settings.Role2Score)
                desired = settings.Role2Id;

        var options = new RequestOptions { CancelToken = ct, AuditLogReason = "Activity XP role update" };

        if (desired != 0 && !user.RoleIds.Contains(desired))
        {
            var role = guild.GetRole(desired) ?? throw new BotCommandException("An activity role no longer exists.");
            DotNext.Optional<string> error = RoleReactionPolicy.ValidateRole(guild, role);

            if (error.HasValue)
                throw new BotCommandException(error.Value);

            await user.AddRoleAsync(desired, options);
        }

        ulong[] candidates = [settings.Role1Id, settings.Role2Id, member.AssignedRoleId];

        var remove = candidates
            .Distinct()
            .Where(id => id != 0 && id != desired && user.RoleIds.Contains(id))
            .ToArray();

        if (remove.Length > 0)
            await user.RemoveRolesAsync(remove, options);

        member.AssignedRoleId = desired;
    }

    private static void RecordAward(BotDbContext db, ActivityMember member, ulong messageId, DateTimeOffset awardedAt, decimal xp)
    {
        db.ActivityAwards.Add(
            new ActivityAward
            {
                GuildId = member.GuildId,
                UserId = member.UserId,
                MessageId = messageId,
                AwardedAt = awardedAt,
                Xp = xp
            });
    }
}
