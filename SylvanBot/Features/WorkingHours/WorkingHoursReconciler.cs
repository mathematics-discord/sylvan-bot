using System.Net;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

using DotNext;

public sealed class WorkingHoursReconciler(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    WorkingHoursRuntime runtime,
    WorkingHoursGuildRules rules,
    TimeProvider time,
    ILogger logger
)
{
    public async Task ReconcileAsync(ulong guildId, CancellationToken ct)
    {
        var state = runtime.ForGuild(guildId);
        await state.Gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);

            if (settings is null)
                return;

            var guild = client.GetGuild(guildId);

            if (!rules.IsReady(guild))
                return;

            // Ready guild role metadata confirms deletions. Never infer deletion while disconnected.
            WorkingHoursEligibleRole[] deleted = settings.EligibleRoles
                .Where(role => guild.GetRole(role.RoleId) is null)
                .ToArray();

            foreach (var role in deleted)
                settings.EligibleRoles.Remove(role);

            if (settings.PingRoleId is { } target && guild.GetRole(target) is null)
                settings.PingRoleId = null;

            if (!settings.IsComplete)
                settings.Enabled = false;

            await db.SaveChangesAsync(ct);
            await ReconcileSavedAsync(db: db, settings: settings, state: state, ct: ct);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public async Task<Optional<int>> ReconcileSavedAsync(
        BotDbContext db,
        WorkingHoursSettings settings,
        WorkingHoursGuildState state,
        CancellationToken ct,
        ulong? onlyMember = null)
    {
        try
        {
            var guild = client.GetGuild(settings.GuildId);
            state.PublishSettingsAndSeed(settings, guild, rules.IsReady(guild));

            if (!rules.IsReady(guild))
                return default;

            Dictionary<ulong, WorkingHoursPreference> preferences = await db.WorkingHours
                .Where(item => item.GuildId == settings.GuildId)
                .ToDictionaryAsync(item => item.MemberId, cancellationToken: ct);

            await ApplyLossesAsync(db: db, state: state, preferences: preferences, onlyMember: onlyMember, ct: ct);
            ICollection<string> problems = rules.ConfigurationProblems(settings: settings, guild: guild);

            if (problems.Count > 0)
            {
                logger.Warning(
                    "Working Hours blocked in guild {GuildId}: {Problems}",
                    propertyValue0: settings.GuildId,
                    string.Join("; ", values: problems));

                return default;
            }

            ISet<ulong> members = state.MemberIds();

            IEnumerable<ulong> scheduled = preferences.Values
                .Where(preference => preference.Enabled)
                .Select(preference => preference.MemberId);

            members.UnionWith(scheduled);

            if (onlyMember.HasValue)
                members = new HashSet<ulong> { onlyMember.Value };

            var pending = await ReconcileMembersAsync(
                db: db,
                settings: settings,
                guild,
                state: state,
                members: members,
                preferences: preferences,
                ct: ct);

            return new Optional<int>(pending);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(exception: ex, "Working Hours saved; reconciliation pending in guild {GuildId}", propertyValue: settings.GuildId);

            return default;
        }
    }

    private static async Task ApplyLossesAsync(
        BotDbContext db,
        WorkingHoursGuildState state,
        Dictionary<ulong, WorkingHoursPreference> preferences,
        ulong? onlyMember,
        CancellationToken ct)
    {
        var losses = state.TakeLosses(onlyMember);

        try
        {
            foreach (var memberId in losses)
                if (preferences.TryGetValue(key: memberId, out var preference))
                    preference.DisableSchedule();

            await db.SaveChangesAsync(ct);
        }
        catch
        {
            state.RestoreLosses(losses);

            throw;
        }
    }

    private async Task<int> ReconcileMembersAsync(
        BotDbContext db,
        WorkingHoursSettings settings,
        SocketGuild guild,
        WorkingHoursGuildState state,
        ISet<ulong> members,
        Dictionary<ulong, WorkingHoursPreference> preferences,
        CancellationToken ct)
    {
        var pending = 0;

        foreach (var memberId in members)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                preferences.TryGetValue(key: memberId, out var preference);

                if (
                    !await ReconcileMemberAsync(
                        db: db,
                        settings: settings,
                        guild: guild,
                        state: state,
                        memberId: memberId,
                        preference: preference,
                        ct: ct))
                    pending++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                pending++;

                logger.Warning(
                    exception: ex,
                    "Working Hours update pending for guild {GuildId}, member {MemberId}",
                    propertyValue0: settings.GuildId,
                    propertyValue1: memberId);
            }
        }

        return pending;
    }

    private async Task<bool> ReconcileMemberAsync(
        BotDbContext db,
        WorkingHoursSettings settings,
        SocketGuild guild,
        WorkingHoursGuildState state,
        ulong memberId,
        WorkingHoursPreference? preference,
        CancellationToken ct)
    {
        if (!rules.IsReady(guild))
            return false;

        var lookup = await ReadMemberAsync(guild: guild, state: state, memberId: memberId, ct: ct);

        if (lookup is MemberLookup.Departed)
        {
            if (preference is { Enabled: true })
            {
                preference.DisableSchedule();
                await db.SaveChangesAsync(ct);
            }

            state.ForgetMember(memberId);

            return true;
        }

        if (lookup is not MemberLookup.Found(var member))
            return false;

        var eligible = WorkingHoursGuildRules.IsEligible(settings: settings, member: member);

        if (!eligible && preference is { Enabled: true })
        {
            preference.DisableSchedule();
            await db.SaveChangesAsync(ct);
        }

        member = guild.GetUser(memberId) ?? member;
        eligible = WorkingHoursGuildRules.IsEligible(settings: settings, member: member);

        var active = settings.Enabled && preference is { Enabled: true, Window: { } window } && window.IsActive(time.GetUtcNow());

        var exempt = settings.EligibleRoles
            .Where(role => role.IsExempt)
            .Any(role => member.RoleIds.Contains(role.RoleId));
        var desired = settings.DesiredPingRole(eligible: eligible, exempt: exempt, preference?.Enabled == true, active: active);

        if (settings.PingRoleId is not { } target)
            return false;

        if (!desired.HasValue || member.RoleIds.Contains(target) == desired.Value)
            return true;

        return await ApplyRoleAsync(settings, guild, member, target, desired.Value, ct);
    }

    private async Task<bool> ApplyRoleAsync(
        WorkingHoursSettings settings,
        SocketGuild guild,
        IGuildUser member,
        ulong target,
        bool desired,
        CancellationToken ct)
    {
        member = guild.GetUser(member.Id) ?? member;

        var canApply = rules.IsReady(guild) && rules.ConfigurationProblems(settings, guild).Count == 0;

        if (!canApply)
            return false;

        if (desired && !WorkingHoursGuildRules.IsEligible(settings: settings, member: member))
            return false;

        var request = new RequestOptions { CancelToken = ct, AuditLogReason = "Working Hours: reconcile configured ping role" };

        if (desired)
            await member.AddRoleAsync(roleId: target, options: request);
        else
            await member.RemoveRoleAsync(roleId: target, options: request);

        return true;
    }

    private async Task<MemberLookup> ReadMemberAsync(SocketGuild guild, WorkingHoursGuildState state, ulong memberId, CancellationToken ct)
    {
        var cached = guild.GetUser(memberId);

        if (cached is not null)
            return new MemberLookup.Found(cached);

        try
        {
            var member = await client.Rest.GetGuildUserAsync(guildId: guild.Id, id: memberId, new RequestOptions { CancelToken = ct });

            if (member is not null)
            {
                // A REST-only member must remain indexed if disabling its preference precedes a failed role removal.
                state.Observe(member);
            }

            return member is null ? new MemberLookup.Unavailable() : new MemberLookup.Found(member);
        }
        catch (HttpException ex) when (ex is { HttpCode: HttpStatusCode.NotFound, DiscordCode: DiscordErrorCode.UnknownMember })
        {
            return new MemberLookup.Departed();
        }
    }

    private static Task<WorkingHoursSettings?> LoadSettingsAsync(BotDbContext db, ulong guildId, CancellationToken ct) =>
        db.WorkingHoursSettings
            .Include(settings => settings.EligibleRoles)
            .SingleOrDefaultAsync(settings => settings.GuildId == guildId, cancellationToken: ct);

    private abstract record MemberLookup
    {
        public sealed record Found(IGuildUser Member) : MemberLookup;

        public sealed record Departed : MemberLookup;

        public sealed record Unavailable : MemberLookup;
    }
}