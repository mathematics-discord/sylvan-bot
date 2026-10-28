using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursConfiguration(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    WorkingHoursRuntime runtime,
    WorkingHoursGuildRules rules,
    WorkingHoursReconciler reconciler
)
{
    public async Task<Result<string>> UpdateAsync(ulong guildId, WorkingConditionsChange change, DotNext.Optional<ulong> roleId, CancellationToken ct)
    {
        var state = runtime.ForGuild(guildId);
        await state.Gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);
            var isNew = settings is null;
            settings ??= new WorkingHoursSettings { GuildId = guildId };
            Result<ValueTuple> validation = ApplyConfigurationChange(settings: settings, change: change, roleId: roleId);

            if (!validation.IsSuccessful)
                return new Result<string>(validation.Error!);

            if (isNew)
                db.WorkingHoursSettings.Add(settings);

            await db.SaveChangesAsync(ct);
            DotNext.Optional<int> pending = await reconciler.ReconcileSavedAsync(db: db, settings: settings, state: state, ct: ct);

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

    private Result<ValueTuple> ApplyConfigurationChange(WorkingHoursSettings settings, WorkingConditionsChange change, DotNext.Optional<ulong> roleId)
    {
        var guild = client.GetGuild(settings.GuildId);

        if (roleId is { HasValue: true, Value: var submittedId })
        {
            var role = guild?.GetRole(submittedId);

            var available = client.ConnectionState == ConnectionState.Connected && guild is { IsConnected: true };

            if (!available || role is null)
            {
                return new Result<ValueTuple>(
                    OperationFailureException.Validation(description: "The selected role is unavailable in this guild. No settings changed."));
            }

            if (change == WorkingConditionsChange.PingRole && role.IsManaged)
            {
                return new Result<ValueTuple>(
                    OperationFailureException.Validation(description: "An integration-managed role cannot be the ping role."));
            }
        }

        Result<ValueTuple> validation = (change, roleId) switch
        {
            (WorkingConditionsChange.PingRole, { HasValue: true, Value: var id }) => settings.SetPingRole(id),
            (WorkingConditionsChange.AddEligible, { HasValue: true, Value: var id }) => settings.ChangeEligibleRole(roleId: id, add: true),
            (WorkingConditionsChange.RemoveEligible, { HasValue: true, Value: var id }) => settings.ChangeEligibleRole(roleId: id, add: false),
            (WorkingConditionsChange.AddExempt, { HasValue: true, Value: var id }) => settings.ChangeExemptRole(roleId: id, add: true),
            (WorkingConditionsChange.RemoveExempt, { HasValue: true, Value: var id }) => settings.ChangeExemptRole(roleId: id, add: false),
            (WorkingConditionsChange.Enable or WorkingConditionsChange.Disable, _) => new Result<ValueTuple>(default(ValueTuple)),
            _ => new Result<ValueTuple>(OperationFailureException.Validation(description: "Choose a role for this configuration change.")),
        };

        if (!validation.IsSuccessful)
            return validation;

        switch (change)
        {
            case WorkingConditionsChange.Enable:
            {
                ICollection<string> problems = rules.ConfigurationProblems(settings: settings, guild: guild);

                if (problems.Count > 0)
                    return new Result<ValueTuple>(OperationFailureException.Validation(description: string.Join(separator: '\n', values: problems)));

                settings.Enabled = true;

                break;
            }
            case WorkingConditionsChange.Disable:
                settings.Enabled = false;

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), actualValue: change, message: null);
        }

        return new Result<ValueTuple>(default(ValueTuple));
    }
}