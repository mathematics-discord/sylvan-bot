using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

using DotNext;

public sealed class WorkingHoursSchedules(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    WorkingHoursRuntime runtime,
    WorkingHoursGuildRules rules,
    WorkingHoursReconciler reconciler
)
{
    public async Task<string> SetScheduleAsync(
        ulong guildId,
        ulong memberId,
        string username,
        bool enable,
        string? start,
        string? end,
        string? timezone,
        CancellationToken ct)
    {
        WorkingHoursGuildState state = runtime.ForGuild(guildId);
        await state.Gate.WaitAsync(ct);

        try
        {
            await using BotDbContext db = await contexts.CreateDbContextAsync(ct);
            WorkingHoursSettings? settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);

            if (settings is not { Enabled: true })
                throw new BotCommandException("Working Hours scheduling is disabled for this guild. No preference or role was changed.");

            Optional<WorkingHoursWindow> window = ValidateSubmission(
                username: username,
                enable: enable,
                start: start,
                end: end,
                timezone: timezone);

            SocketGuild guild = client.GetGuild(guildId);

            if (!rules.IsReady(guild))
                throw new BotCommandException("Current guild/member data is unavailable. No preference was changed.");

            if (!settings.IsComplete)
                throw new BotCommandException("Working Hours configuration is incomplete. No preference was changed.");

            SocketGuildUser? member = guild.GetUser(memberId);

            if (member is null)
                throw new BotCommandException("Current member data is unavailable. No preference was changed.");

            if (!WorkingHoursGuildRules.IsEligible(settings: settings, member: member))
                throw new BotCommandException("Only members with a configured eligible staff role may set working hours.");

            await SaveScheduleAsync(db: db, state: state, guildId: guildId, memberId: memberId, window: window, ct: ct);

            Optional<int> pending = await reconciler.ReconcileSavedAsync(
                db: db,
                settings: settings,
                state: state,
                ct: ct,
                onlyMember: memberId);

            var response = window is { HasValue: true, Value: var accepted }
                ? string.Join("\n\n", WorkingHoursResponses.Accepted(accepted))
                : "Your schedule is disabled. Ordinary staff require the ping role; exempt seniors retain manual control.";

            return "Saved. " + WorkingHoursPresentation.CompletionMessage("Role reconciliation", pending: pending) + "\n\n" + response;
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private static Optional<WorkingHoursWindow> ValidateSubmission(
        string username,
        bool enable,
        string? start,
        string? end,
        string? timezone)
    {
        Optional<WorkingHoursWindow> window = default;

        if (enable)
        {
            if (start is null || end is null)
                throw new BotCommandException("Enabling requires both start and end on every submission.");

            WorkingHoursWindow parsed = WorkingHoursTimeParser.Parse(start: start, end: end, timezone: timezone);

            window = new Optional<WorkingHoursWindow>(parsed);
            var rejection = WorkingHoursResponses.Rejection(window: parsed, username: username);

            if (rejection is not null)
                throw new BotCommandException(rejection);
        }
        else if (start is not null || end is not null || timezone is not null)
            throw new BotCommandException("Disabling accepts no start, end or timezone options.");

        return window;
    }

    private static async Task SaveScheduleAsync(
        BotDbContext db,
        WorkingHoursGuildState state,
        ulong guildId,
        ulong memberId,
        Optional<WorkingHoursWindow> window,
        CancellationToken ct)
    {
        WorkingHoursPreference? preference = await db.WorkingHours
            .SingleOrDefaultAsync(
                item => item.GuildId == guildId && item.MemberId == memberId,
                cancellationToken: ct);

        // Consume only losses preceding this explicit command; later gateway losses remain pending.
        var priorLoss = state.ConsumeLoss(memberId);

        try
        {
            if (window is { HasValue: true, Value: var schedule })
            {
                if (preference is null)
                {
                    preference = new WorkingHoursPreference(guildId: guildId, memberId: memberId);
                    db.WorkingHours.Add(preference);
                }

                preference.EnableSchedule(schedule);
            }
            else
            {
                preference?.DisableSchedule();
            }

            await db.SaveChangesAsync(ct);
        }
        catch when (priorLoss)
        {
            state.RestoreLosses([memberId]);
            throw;
        }
    }

    private static Task<WorkingHoursSettings?> LoadSettingsAsync(BotDbContext db, ulong guildId, CancellationToken ct) =>
        db.WorkingHoursSettings
            .Include(settings => settings.EligibleRoles)
            .SingleOrDefaultAsync(settings => settings.GuildId == guildId, cancellationToken: ct);
}