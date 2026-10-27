using System.Collections.Concurrent;
using System.Net;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursCoordinator(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    TimeProvider time,
    ILogger logger)
{
    private readonly ConcurrentDictionary<ulong, GuildState> _guilds = new();

    public async Task<string> ShowAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var settings = await db.WorkingHoursSettings
           .AsNoTracking()
           .Include(item => item.EligibleRoles)
           .SingleOrDefaultAsync(item => item.GuildId == guildId, cancellationToken: ct);

        settings ??= new WorkingHoursSettings { GuildId = guildId };

        IEnumerable<string> eligible = settings.EligibleRoles
           .OrderBy(role => role.RoleId)
           .Select(role => $"<@&{role.RoleId}>");

        IEnumerable<string> exemptions = settings.EligibleRoles
           .Where(role => role.IsExempt)
           .OrderBy(role => role.RoleId)
           .Select(role => $"<@&{role.RoleId}>");
        var          guild    = client.GetGuild(guildId);
        List<string> problems = ConfigurationProblems(settings: settings, guild: guild);

        if (!IsReady(guild))
            problems.Add("Member data is not ready; role reconciliation is pending.");

        var target = settings.PingRoleId is { } id ? $"<@&{id}>" : "unset";

        return $"Working Hours: {(settings.Enabled ? "enabled" : "disabled")}\n"
            + $"Ping role: {target}\nEligible roles: {FormatRoles(eligible)}\nExemption roles: {FormatRoles(exemptions)}\n"
            + (problems.Count == 0 ? "Configuration is valid." : string.Join(separator: '\n', values: problems));
    }

    public async Task<ErrorOr<string>> UpdateAsync(
        ulong guildId,
        WorkingConditionsChange change,
        ulong? roleId,
        CancellationToken ct)
    {
        var state = _guilds.GetOrAdd(key: guildId, _ => new GuildState());
        await state.Gate.WaitAsync(ct);

        try
        {
            await using var db       = await contexts.CreateDbContextAsync(ct);
            var             settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);
            var             isNew    = settings is null;
            settings ??= new WorkingHoursSettings { GuildId = guildId };
            ErrorOr<Success> validation = ApplyConfigurationChange(settings: settings, change: change, roleId: roleId);

            if (validation.IsError)
                return validation.Errors;

            if (isNew)
                db.WorkingHoursSettings.Add(settings);

            await db.SaveChangesAsync(ct);
            var pending = await ReconcileSavedAsync(db: db, settings: settings, state: state, ct: ct);

            var message = change switch
            {
                WorkingConditionsChange.Disable => "Scheduling disabled. Personal schedules have been retained.",
                WorkingConditionsChange.Enable  => "Scheduling enabled. Saved enabled schedules resume from the current time.",
                WorkingConditionsChange.PingRole =>
                    "Ping role saved. Existing assignments of the old role are unchanged; no mass role migration is performed.",
                _ => "Working conditions saved.",
            };

            var action = settings.Enabled ? "Role reconciliation" : "Default-role restoration";

            return message + "\n" + CompletionMessage(action: action, pending: pending);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public async Task<ErrorOr<string>> SetScheduleAsync(
        ulong guildId,
        ulong memberId,
        string username,
        bool enable,
        string? start,
        string? end,
        string? timezone,
        CancellationToken ct)
    {
        var state = _guilds.GetOrAdd(key: guildId, _ => new GuildState());
        await state.Gate.WaitAsync(ct);

        try
        {
            await using var db       = await contexts.CreateDbContextAsync(ct);
            var             settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);

            if (settings is not { Enabled: true })
                return Error.Validation(description: "Working Hours scheduling is disabled for this guild. No preference or role was changed.");

            ErrorOr<WorkingHoursWindow?> validation = ValidateSubmission(username: username, enable: enable, start: start, end: end, timezone: timezone);

            if (validation.IsError)
                return validation.Errors;

            var window = validation.Value;
            var guild  = client.GetGuild(guildId);

            if (!IsReady(guild))
                return Error.Failure(description: "Current guild/member data is unavailable. No preference was changed.");

            if (!settings.IsComplete)
                return Error.Validation(description: "Working Hours configuration is incomplete. No preference was changed.");

            var member = guild!.GetUser(memberId);

            if (member is null)
                return Error.Failure(description: "Current member data is unavailable. No preference was changed.");

            if (!IsEligible(settings: settings, member: member))
                return Error.Unauthorized(description: "Only members with a configured eligible staff role may set working hours.");

            await SaveScheduleAsync(db: db, state: state, guildId: guildId, memberId: memberId, window: window, ct: ct);
            var pending = await ReconcileSavedAsync(db: db, settings: settings, state: state, ct: ct, onlyMember: memberId);

            var response = window is null
                ? "Your schedule is disabled. Ordinary staff require the ping role; exempt seniors retain manual control."
                : string.Join("\n\n", WorkingHoursResponses.Accepted(window));

            return "Saved. " + CompletionMessage("Role reconciliation", pending: pending) + "\n\n" + response;
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public async Task ReconcileAsync(ulong guildId, CancellationToken ct)
    {
        var state = _guilds.GetOrAdd(key: guildId, _ => new GuildState());
        await state.Gate.WaitAsync(ct);

        try
        {
            await using var db       = await contexts.CreateDbContextAsync(ct);
            var             settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);

            if (settings is null)
                return;

            var guild = client.GetGuild(guildId);

            if (!IsReady(guild))
                return;

            // Ready guild role metadata confirms deletions. Never infer deletion while disconnected.
            WorkingHoursEligibleRole[] deleted = settings.EligibleRoles
               .Where(role => guild!.GetRole(role.RoleId) is null)
               .ToArray();

            foreach (var role in deleted)
                settings.EligibleRoles.Remove(role);

            if (settings.PingRoleId is { } target && guild!.GetRole(target) is null)
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

    public void DataReady(ulong? guildId = null)
    {
        foreach (KeyValuePair<ulong, GuildState> pair in _guilds)
        {
            if (guildId.HasValue && pair.Key != guildId.Value)
                continue;

            lock (pair.Value.Sync)
            {
                pair.Value.Seeded = false;
            }
        }
    }

    public void MemberChanged(SocketGuildUser member)
    {
        if (!_guilds.TryGetValue(key: member.Guild.Id, out var state))
            return;

        lock (state.Sync)
        {
            IndexMember(state: state, member: member);
        }
    }

    public void MemberLeft(ulong guildId, ulong memberId)
    {
        if (!_guilds.TryGetValue(key: guildId, out var state))
            return;

        lock (state.Sync)
        {
            if (state.Members.Remove(memberId))
                state.Losses.Add(memberId);
        }
    }

    private static ErrorOr<WorkingHoursWindow?> ValidateSubmission(
        string username,
        bool enable,
        string? start,
        string? end,
        string? timezone)
    {
        WorkingHoursWindow? window = null;

        if (enable)
        {
            if (start is null || end is null)
                return Error.Validation(description: "Enabling requires both start and end on every submission.");

            ErrorOr<WorkingHoursWindow> parsed = WorkingHoursTimeParser.Parse(start: start, end: end, timezone: timezone);

            if (parsed.IsError)
                return parsed.Errors;

            window = parsed.Value;
            var rejection = WorkingHoursResponses.Rejection(window: window, username: username);

            if (rejection is not null)
                return Error.Validation(description: rejection);
        }
        else if (start is not null || end is not null || timezone is not null)
            return Error.Validation(description: "Disabling accepts no start, end or timezone options.");

        return window;
    }

    private static async Task SaveScheduleAsync(
        BotDbContext db,
        GuildState state,
        ulong guildId,
        ulong memberId,
        WorkingHoursWindow? window,
        CancellationToken ct)
    {
        var preference = await db.WorkingHours
           .SingleOrDefaultAsync(item => item.GuildId == guildId && item.MemberId == memberId, cancellationToken: ct);

        // Consume only losses preceding this explicit command; later gateway losses remain pending.
        bool priorLoss;

        lock (state.Sync)
        {
            priorLoss = state.Losses.Remove(memberId);
        }

        try
        {
            if (window is not null)
            {
                if (preference is null)
                {
                    preference = new WorkingHoursPreference(guildId: guildId, memberId: memberId);
                    db.WorkingHours.Add(preference);
                }

                preference.EnableSchedule(window);
            }
            else
                preference?.DisableSchedule();

            await db.SaveChangesAsync(ct);
        }
        catch when (priorLoss)
        {
            lock (state.Sync)
            {
                state.Losses.Add(memberId);
            }

            throw;
        }
    }

    private static void IndexMember(GuildState state, IGuildUser member)
    {
        var eligible = state.EligibleRoles.Overlaps(member.RoleIds);

        if (state.Members.TryGetValue(key: member.Id, out var previouslyEligible) && previouslyEligible && !eligible)
            state.Losses.Add(member.Id);

        var hasTarget = state.PingRoleId is { } target && member.RoleIds.Contains(target);

        if (eligible || hasTarget)
            state.Members[member.Id] = eligible;
        else
            state.Members.Remove(member.Id);
    }

    private static Task<WorkingHoursSettings?> LoadSettingsAsync(BotDbContext db, ulong guildId, CancellationToken ct) =>
        db.WorkingHoursSettings
           .Include(settings => settings.EligibleRoles)
           .SingleOrDefaultAsync(settings => settings.GuildId == guildId, cancellationToken: ct);

    private static string FormatRoles(IEnumerable<string> roles)
    {
        var text = string.Join(", ", values: roles);

        return text.Length == 0 ? "none" : text;
    }

    private static string CompletionMessage(string action, int? pending) => pending switch
    {
        null => $"{action} pending: configuration or member data is unavailable. Reconciliation will retry.",
        0    => $"{action} completed for the currently observed members.",
        _    => $"{action} pending for {pending} member(s). Reconciliation will retry.",
    };

    private static bool IsEligible(WorkingHoursSettings settings, IGuildUser member)
    {
        return settings.EligibleRoles
           .Any(role => member.RoleIds.Contains(role.RoleId));
    }

    private static async Task ApplyLossesAsync(
        BotDbContext db,
        GuildState state,
        Dictionary<ulong, WorkingHoursPreference> preferences,
        ulong? onlyMember,
        CancellationToken ct)
    {
        ulong[] losses;

        lock (state.Sync)
        {
            losses = state.Losses
               .Where(id => !onlyMember.HasValue || id == onlyMember.Value)
               .ToArray();
            state.Losses.ExceptWith(losses);
        }

        try
        {
            foreach (var memberId in losses)
                if (preferences.TryGetValue(key: memberId, out var preference))
                    preference.DisableSchedule();

            await db.SaveChangesAsync(ct);
        }
        catch
        {
            lock (state.Sync)
            {
                state.Losses.UnionWith(losses);
            }

            throw;
        }
    }

    private ErrorOr<Success> ApplyConfigurationChange(
        WorkingHoursSettings settings,
        WorkingConditionsChange change,
        ulong? roleId)
    {
        var guild = client.GetGuild(settings.GuildId);

        if (roleId is { } submittedId)
        {
            var role = guild?.GetRole(submittedId);

            if (client.ConnectionState != ConnectionState.Connected || guild is not { IsConnected: true } || role is null)
                return Error.Validation(description: "The selected role is unavailable in this guild. No settings changed.");

            if (change == WorkingConditionsChange.PingRole && role.IsManaged)
                return Error.Validation(description: "An integration-managed role cannot be the ping role.");
        }

        ErrorOr<Success> validation = change switch
        {
            WorkingConditionsChange.PingRole       => settings.SetPingRole(roleId!.Value),
            WorkingConditionsChange.AddEligible    => settings.ChangeEligibleRole(roleId: roleId!.Value, add: true),
            WorkingConditionsChange.RemoveEligible => settings.ChangeEligibleRole(roleId: roleId!.Value, add: false),
            WorkingConditionsChange.AddExempt      => settings.ChangeExemptRole(roleId: roleId!.Value, add: true),
            WorkingConditionsChange.RemoveExempt   => settings.ChangeExemptRole(roleId: roleId!.Value, add: false),
            _                                      => Result.Success,
        };

        if (validation.IsError)
            return validation;

        if (change == WorkingConditionsChange.Enable)
        {
            List<string> problems = ConfigurationProblems(settings: settings, guild: guild);

            if (problems.Count > 0)
                return Error.Validation(description: string.Join(separator: '\n', values: problems));

            settings.Enabled = true;
        }
        else if (change == WorkingConditionsChange.Disable)
            settings.Enabled = false;

        return Result.Success;
    }

    private List<string> ConfigurationProblems(WorkingHoursSettings settings, SocketGuild? guild)
    {
        List<string> problems = [];

        if (!settings.PingRoleId.HasValue)
            problems.Add("Set a ping role before enabling.");

        if (settings.EligibleRoles.Count == 0)
            problems.Add("Add at least one eligible role before enabling.");

        if (client.ConnectionState != ConnectionState.Connected || guild is not { IsConnected: true })
        {
            problems.Add("Guild data is unavailable; role references and bot permissions cannot be verified.");

            return problems;
        }

        problems.AddRange(
            settings.EligibleRoles
               .Where(entry => entry.RoleId == guild.Id || entry.RoleId == settings.PingRoleId || guild.GetRole(entry.RoleId) is null)
               .Select(entry => $"Eligible role {entry.RoleId} is missing or invalid."));

        if (settings.PingRoleId is not { } targetId)
            return problems;

        var target = guild.GetRole(targetId);
        var bot    = guild.CurrentUser;

        var manageable = target is not null
            && target.Id != guild.Id
            && !target.IsManaged
            && bot is not null
            && bot.GuildPermissions.ManageRoles
            && bot.Hierarchy > target.Position;

        if (!manageable)
            problems.Add("The ping role is missing, @everyone, integration-managed or beyond the bot's Manage Roles permission/hierarchy.");

        return problems;
    }

    private bool IsReady(SocketGuild? guild) =>
        client.ConnectionState == ConnectionState.Connected
        && guild is { IsConnected: true, HasAllMembers: true };

    private async Task<int?> ReconcileSavedAsync(
        BotDbContext db,
        WorkingHoursSettings settings,
        GuildState state,
        CancellationToken ct,
        ulong? onlyMember = null)
    {
        try
        {
            var guild = client.GetGuild(settings.GuildId);
            PublishSettingsAndSeed(settings: settings, guild: guild, state: state);

            if (!IsReady(guild))
                return null;

            Dictionary<ulong, WorkingHoursPreference> preferences = await db.WorkingHours
               .Where(item => item.GuildId == settings.GuildId)
               .ToDictionaryAsync(item => item.MemberId, cancellationToken: ct);

            await ApplyLossesAsync(db: db, state: state, preferences: preferences, onlyMember: onlyMember, ct: ct);
            List<string> problems = ConfigurationProblems(settings: settings, guild: guild);

            if (problems.Count > 0)
            {
                logger.Warning("Working Hours blocked in guild {GuildId}: {Problems}", propertyValue0: settings.GuildId, string.Join("; ", values: problems));

                return null;
            }

            HashSet<ulong> members;

            lock (state.Sync)
            {
                members = new HashSet<ulong>(state.Members.Keys);
            }

            IEnumerable<ulong> scheduled = preferences.Values
               .Where(preference => preference.Enabled)
               .Select(preference => preference.MemberId);

            members.UnionWith(scheduled);

            if (onlyMember.HasValue)
                members = [onlyMember.Value];

            return await ReconcileMembersAsync(db: db, settings: settings, guild!, state: state, members: members, preferences: preferences, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(exception: ex, "Working Hours saved; reconciliation pending in guild {GuildId}", propertyValue: settings.GuildId);

            return null;
        }
    }

    private async Task<int> ReconcileMembersAsync(
        BotDbContext db,
        WorkingHoursSettings settings,
        SocketGuild guild,
        GuildState state,
        HashSet<ulong> members,
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

                if (!await ReconcileMemberAsync(db: db, settings: settings, guild: guild, state: state, memberId: memberId, preference: preference, ct: ct))
                    pending++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                pending++;
                logger.Warning(exception: ex, "Working Hours update pending for guild {GuildId}, member {MemberId}", propertyValue0: settings.GuildId, propertyValue1: memberId);
            }
        }

        return pending;
    }

    private void PublishSettingsAndSeed(WorkingHoursSettings settings, SocketGuild? guild, GuildState state)
    {
        HashSet<ulong> eligibleRoles = settings.EligibleRoles
           .Select(role => role.RoleId)
           .ToHashSet();

        lock (state.Sync)
        {
            var changed = state.PingRoleId != settings.PingRoleId || !state.EligibleRoles.SetEquals(eligibleRoles);
            state.PingRoleId    = settings.PingRoleId;
            state.EligibleRoles = eligibleRoles;

            if (changed)
                state.Seeded = false;

            if (state.Seeded || !IsReady(guild))
                return;

            var previousEligible = state.Members
               .Where(pair => pair.Value)
               .Select(pair => pair.Key)
               .ToArray();
            state.Members.Clear();

            foreach (var member in guild!.Users)
            {
                IReadOnlyCollection<ulong> roles     = ((IGuildUser)member).RoleIds;
                var                        eligible  = eligibleRoles.Overlaps(roles);
                var                        hasTarget = settings.PingRoleId is { } target && roles.Contains(target);

                if (eligible || hasTarget)
                    state.Members[member.Id] = eligible;
            }

            foreach (var memberId in previousEligible)
                // A missing member is unknown; only a present member proves loss of eligibility here.
                if (guild.GetUser(memberId) is not null && !state.Members.GetValueOrDefault(memberId))
                    state.Losses.Add(memberId);

            state.Seeded = true;
        }
    }

    private async Task<bool> ReconcileMemberAsync(
        BotDbContext db,
        WorkingHoursSettings settings,
        SocketGuild guild,
        GuildState state,
        ulong memberId,
        WorkingHoursPreference? preference,
        CancellationToken ct)
    {
        if (!IsReady(guild))
            return false;

        var (member, departed) = await ReadMemberAsync(guild: guild, state: state, memberId: memberId, ct: ct);

        if (departed)
        {
            if (preference is { Enabled: true })
            {
                preference.DisableSchedule();
                await db.SaveChangesAsync(ct);
            }

            lock (state.Sync)
            {
                state.Members.Remove(memberId);
            }

            return true;
        }

        if (member is null)
            return false;

        var eligible = IsEligible(settings: settings, member: member);

        if (!eligible && preference is { Enabled: true })
        {
            preference.DisableSchedule();
            await db.SaveChangesAsync(ct);
        }

        member   = guild.GetUser(memberId) ?? member;
        eligible = IsEligible(settings: settings, member: member);

        var active  = settings.Enabled && preference is { Enabled: true } && preference.Window!.IsActive(time.GetUtcNow());
        var exempt  = settings.EligibleRoles.Any(role => role.IsExempt && member.RoleIds.Contains(role.RoleId));
        var desired = settings.DesiredPingRole(eligible: eligible, exempt: exempt, preference?.Enabled == true, active: active);
        var target  = settings.PingRoleId!.Value;

        if (!desired.HasValue || member.RoleIds.Contains(target) == desired.Value)
            return true;

        member = guild.GetUser(memberId) ?? member;

        if (!IsReady(guild) || ConfigurationProblems(settings: settings, guild: guild).Count > 0)
            return false;

        if (desired.Value && !IsEligible(settings: settings, member: member))
            return false;

        var request = new RequestOptions { CancelToken = ct, AuditLogReason = "Working Hours: reconcile configured ping role" };

        if (desired.Value)
            await member.AddRoleAsync(roleId: target, options: request);
        else
            await member.RemoveRoleAsync(roleId: target, options: request);

        return true;
    }

    private async Task<(IGuildUser? Member, bool Departed)> ReadMemberAsync(
        SocketGuild guild,
        GuildState state,
        ulong memberId,
        CancellationToken ct)
    {
        var cached = guild.GetUser(memberId);

        if (cached is not null)
            return (cached, false);

        try
        {
            var member = await client.Rest.GetGuildUserAsync(guildId: guild.Id, id: memberId, new RequestOptions { CancelToken = ct });

            if (member is not null)
            {
                // A REST-only member must remain indexed if disabling its preference precedes a failed role removal.
                lock (state.Sync)
                {
                    IndexMember(state: state, member: member);
                }
            }

            return (member, false);
        }
        catch (HttpException ex) when (ex is { HttpCode: HttpStatusCode.NotFound, DiscordCode: DiscordErrorCode.UnknownMember })
        {
            return (null, true);
        }
    }

    private sealed class GuildState
    {
        public SemaphoreSlim Gate { get; } = new(initialCount: 1, maxCount: 1);

        public Lock Sync { get; } = new();

        public Dictionary<ulong, bool> Members { get; } = new();

        public HashSet<ulong> Losses { get; } = [];

        public HashSet<ulong> EligibleRoles { get; set; } = [];

        public ulong? PingRoleId { get; set; }

        public bool Seeded { get; set; }
    }
}