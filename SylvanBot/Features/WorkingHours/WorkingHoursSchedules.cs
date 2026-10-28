using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursSchedules(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    WorkingHoursRuntime runtime,
    WorkingHoursGuildRules rules,
    WorkingHoursReconciler reconciler
)
{
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
        var state = runtime.ForGuild(guildId);
        await state.Gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);

            if (settings is not { Enabled: true })
                return Error.Validation(description: "Working Hours scheduling is disabled for this guild. No preference or role was changed.");

            ErrorOr<Option<WorkingHoursWindow>> validation = ValidateSubmission(
                username: username,
                enable: enable,
                start: start,
                end: end,
                timezone: timezone);

            if (validation.IsError)
                return validation.Errors;

            Option<WorkingHoursWindow> window = validation.Value;
            var guild = client.GetGuild(guildId);

            if (!rules.IsReady(guild))
                return Error.Failure(description: "Current guild/member data is unavailable. No preference was changed.");

            if (!settings.IsComplete)
                return Error.Validation(description: "Working Hours configuration is incomplete. No preference was changed.");

            var member = guild.GetUser(memberId);

            if (member is null)
                return Error.Failure(description: "Current member data is unavailable. No preference was changed.");

            if (!WorkingHoursGuildRules.IsEligible(settings: settings, member: member))
                return Error.Unauthorized(description: "Only members with a configured eligible staff role may set working hours.");

            await SaveScheduleAsync(db: db, state: state, guildId: guildId, memberId: memberId, window: window, ct: ct);
            Option<int> pending = await reconciler.ReconcileSavedAsync(db: db, settings: settings, state: state, ct: ct, onlyMember: memberId);

            var response = window.Value is Some<WorkingHoursWindow>(var accepted)
                ? string.Join("\n\n", WorkingHoursResponses.Accepted(accepted))
                : "Your schedule is disabled. Ordinary staff require the ping role; exempt seniors retain manual control.";

            return "Saved. " + WorkingHoursPresentation.CompletionMessage("Role reconciliation", pending: pending) + "\n\n" + response;
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private static ErrorOr<Option<WorkingHoursWindow>> ValidateSubmission(string username, bool enable, string? start, string? end, string? timezone)
    {
        Option<WorkingHoursWindow> window = new None();

        if (enable)
        {
            if (start is null || end is null)
                return Error.Validation(description: "Enabling requires both start and end on every submission.");

            ErrorOr<WorkingHoursWindow> parsed = WorkingHoursTimeParser.Parse(start: start, end: end, timezone: timezone);

            if (parsed.IsError)
                return parsed.Errors;

            window = new Some<WorkingHoursWindow>(parsed.Value);
            var rejection = WorkingHoursResponses.Rejection(window: parsed.Value, username: username);

            if (rejection is not null)
                return Error.Validation(description: rejection);
        }
        else if (start is not null || end is not null || timezone is not null)
            return Error.Validation(description: "Disabling accepts no start, end or timezone options.");

        return window;
    }

    private static async Task SaveScheduleAsync(
        BotDbContext db,
        WorkingHoursGuildState state,
        ulong guildId,
        ulong memberId,
        Option<WorkingHoursWindow> window,
        CancellationToken ct)
    {
        var preference = await db.WorkingHours
            .SingleOrDefaultAsync(
                item => item.GuildId == guildId && item.MemberId == memberId,
                cancellationToken: ct);
        // Consume only losses preceding this explicit command; later gateway losses remain pending.
        var priorLoss = state.ConsumeLoss(memberId);

        try
        {
            if (window.Value is Some<WorkingHoursWindow>(var schedule))
            {
                if (preference is null)
                {
                    preference = new WorkingHoursPreference(guildId: guildId, memberId: memberId);
                    db.WorkingHours.Add(preference);
                }

                preference.EnableSchedule(schedule);
            }
            else
                preference?.DisableSchedule();

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