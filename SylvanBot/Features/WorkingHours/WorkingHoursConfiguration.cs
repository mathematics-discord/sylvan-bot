using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursConfiguration(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    WorkingHoursRuntime runtime,
    WorkingHoursGuildRules rules,
    WorkingHoursReconciler reconciler
)
{
    public async Task<ErrorOr<string>> UpdateAsync(ulong guildId, WorkingConditionsChange change, Option<ulong> roleId, CancellationToken ct)
    {
        var state = runtime.ForGuild(guildId);
        await state.Gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);
            var isNew = settings is null;
            settings ??= new WorkingHoursSettings { GuildId = guildId };
            ErrorOr<Success> validation = ApplyConfigurationChange(settings: settings, change: change, roleId: roleId);

            if (validation.IsError)
                return validation.Errors;

            if (isNew)
                db.WorkingHoursSettings.Add(settings);

            await db.SaveChangesAsync(ct);
            Option<int> pending = await reconciler.ReconcileSavedAsync(db: db, settings: settings, state: state, ct: ct);

            var message = change switch
            {
                WorkingConditionsChange.Disable => "Scheduling disabled. Personal schedules have been retained.",
                WorkingConditionsChange.Enable => "Scheduling enabled. Saved enabled schedules resume from the current time.",
                WorkingConditionsChange.PingRole =>
                    "Ping role saved. Existing assignments of the old role are unchanged; no mass role migration is performed.",
                _ => "Working conditions saved.",
            };

            var action = settings.Enabled ? "Role reconciliation" : "Default-role restoration";

            return message + "\n" + WorkingHoursPresentation.CompletionMessage(action: action, pending: pending);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private static Task<WorkingHoursSettings?> LoadSettingsAsync(BotDbContext db, ulong guildId, CancellationToken ct) =>
        db.WorkingHoursSettings
            .Include(settings => settings.EligibleRoles)
            .SingleOrDefaultAsync(settings => settings.GuildId == guildId, cancellationToken: ct);

    private ErrorOr<Success> ApplyConfigurationChange(WorkingHoursSettings settings, WorkingConditionsChange change, Option<ulong> roleId)
    {
        var guild = client.GetGuild(settings.GuildId);

        if (roleId.Value is Some<ulong>(var submittedId))
        {
            var role = guild?.GetRole(submittedId);

            var available = client.ConnectionState == ConnectionState.Connected && guild is { IsConnected: true };

            if (!available || role is null)
                return Error.Validation(description: "The selected role is unavailable in this guild. No settings changed.");

            if (change == WorkingConditionsChange.PingRole && role.IsManaged)
                return Error.Validation(description: "An integration-managed role cannot be the ping role.");
        }

        ErrorOr<Success> validation = (change, roleId.Value) switch
        {
            (WorkingConditionsChange.PingRole, Some<ulong>(var id)) => settings.SetPingRole(id),
            (WorkingConditionsChange.AddEligible, Some<ulong>(var id)) => settings.ChangeEligibleRole(id, add: true),
            (WorkingConditionsChange.RemoveEligible, Some<ulong>(var id)) => settings.ChangeEligibleRole(id, add: false),
            (WorkingConditionsChange.AddExempt, Some<ulong>(var id)) => settings.ChangeExemptRole(id, add: true),
            (WorkingConditionsChange.RemoveExempt, Some<ulong>(var id)) => settings.ChangeExemptRole(id, add: false),
            (WorkingConditionsChange.Enable or WorkingConditionsChange.Disable, _) => Result.Success,
            _ => Error.Validation(description: "Choose a role for this configuration change."),
        };

        if (validation.IsError)
            return validation;

        switch (change)
        {
            case WorkingConditionsChange.Enable:
            {
                ICollection<string> problems = rules.ConfigurationProblems(settings: settings, guild: guild);

                if (problems.Count > 0)
                    return Error.Validation(description: string.Join(separator: '\n', values: problems));

                settings.Enabled = true;

                break;
            }
            case WorkingConditionsChange.Disable:
                settings.Enabled = false;

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, message: null);
        }

        return Result.Success;
    }
}