using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursSchedules(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    WorkingHoursRuntime runtime,
    WorkingHoursGuildRules rules,
    WorkingHoursReconciler reconciler
)
{
    public async Task<Result<string>> SetScheduleAsync(
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
            {
                return new Result<string>(
                    OperationFailureException.Validation(
                        description: "Working Hours scheduling is disabled for this guild. No preference or role was changed."));
            }

            Result<DotNext.Optional<WorkingHoursWindow>> validation = ValidateSubmission(
                username: username,
                enable: enable,
                start: start,
                end: end,
                timezone: timezone);

            if (!validation.IsSuccessful)
                return new Result<string>(validation.Error!);

            DotNext.Optional<WorkingHoursWindow> window = validation.Value;
            var guild = client.GetGuild(guildId);

            if (!rules.IsReady(guild))
            {
                return new Result<string>(
                    OperationFailureException.Failure(description: "Current guild/member data is unavailable. No preference was changed."));
            }

            if (!settings.IsComplete)
            {
                return new Result<string>(
                    OperationFailureException.Validation(description: "Working Hours configuration is incomplete. No preference was changed."));
            }

            var member = guild.GetUser(memberId);

            if (member is null)
            {
                return new Result<string>(
                    OperationFailureException.Failure(description: "Current member data is unavailable. No preference was changed."));
            }

            if (!WorkingHoursGuildRules.IsEligible(settings: settings, member: member))
            {
                return new Result<string>(
                    OperationFailureException.Unauthorized(description: "Only members with a configured eligible staff role may set working hours."));
            }

            await SaveScheduleAsync(db: db, state: state, guildId: guildId, memberId: memberId, window: window, ct: ct);

            DotNext.Optional<int> pending = await reconciler.ReconcileSavedAsync(
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

    private static Result<DotNext.Optional<WorkingHoursWindow>> ValidateSubmission(
        string username,
        bool enable,
        string? start,
        string? end,
        string? timezone)
    {
        DotNext.Optional<WorkingHoursWindow> window = default;

        if (enable)
        {
            if (start is null || end is null)
            {
                return new Result<DotNext.Optional<WorkingHoursWindow>>(
                    OperationFailureException.Validation(description: "Enabling requires both start and end on every submission."));
            }

            Result<WorkingHoursWindow> parsed = WorkingHoursTimeParser.Parse(start: start, end: end, timezone: timezone);

            if (!parsed.IsSuccessful)
                return new Result<DotNext.Optional<WorkingHoursWindow>>(parsed.Error!);

            window = new DotNext.Optional<WorkingHoursWindow>(parsed.Value);
            var rejection = WorkingHoursResponses.Rejection(window: parsed.Value, username: username);

            if (rejection is not null)
                return new Result<DotNext.Optional<WorkingHoursWindow>>(OperationFailureException.Validation(description: rejection));
        }
        else if (start is not null || end is not null || timezone is not null)
        {
            return new Result<DotNext.Optional<WorkingHoursWindow>>(
                OperationFailureException.Validation(description: "Disabling accepts no start, end or timezone options."));
        }

        return window;
    }

    private static async Task SaveScheduleAsync(
        BotDbContext db,
        WorkingHoursGuildState state,
        ulong guildId,
        ulong memberId,
        DotNext.Optional<WorkingHoursWindow> window,
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