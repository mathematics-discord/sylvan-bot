using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

using DotNext;

public sealed class WorkingHoursConfiguration(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    WorkingHoursRuntime runtime,
    WorkingHoursGuildRules rules,
    WorkingHoursReconciler reconciler
)
{
    public async Task<string> UpdateAsync(ulong guildId, WorkingConditionsChange change, Optional<ulong> roleId, CancellationToken ct)
    {
        WorkingHoursGuildState state = runtime.ForGuild(guildId);
        await state.Gate.WaitAsync(ct);

        try
        {
            await using BotDbContext db = await contexts.CreateDbContextAsync(ct);
            WorkingHoursSettings? settings = await LoadSettingsAsync(db: db, guildId: guildId, ct: ct);
            var isNew = settings is null;
            settings ??= new WorkingHoursSettings { GuildId = guildId };
            ApplyConfigurationChange(settings: settings, change: change, roleId: roleId);

            if (isNew)
                db.WorkingHoursSettings.Add(settings);

            await db.SaveChangesAsync(ct);
            Optional<int> pending = await reconciler.ReconcileSavedAsync(db: db, settings: settings, state: state, ct: ct);

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

    private void ApplyConfigurationChange(WorkingHoursSettings settings, WorkingConditionsChange change, Optional<ulong> roleId)
    {
        SocketGuild? guild = client.GetGuild(settings.GuildId);

        if (roleId is { HasValue: true, Value: var submittedId })
        {
            SocketRole? role = guild?.GetRole(submittedId);

            var available = client.ConnectionState == ConnectionState.Connected && guild is { IsConnected: true };

            if (!available || role is null)
                throw new BotCommandException("The selected role is unavailable in this guild. No settings changed.");

            if (change == WorkingConditionsChange.PingRole && role.IsManaged)
                throw new BotCommandException("An integration-managed role cannot be the ping role.");
        }

        switch (change)
        {
            case WorkingConditionsChange.Enable:
            {
                ICollection<string> problems = rules.ConfigurationProblems(settings: settings, guild: guild);

                if (problems.Count > 0)
                    throw new BotCommandException(string.Join(separator: '\n', values: problems));

                settings.Enabled = true;

                break;
            }
            case WorkingConditionsChange.Disable:
                settings.Enabled = false;

                break;
            case WorkingConditionsChange.PingRole when roleId.HasValue:
                settings.SetPingRole(roleId.Value);
                break;
            case WorkingConditionsChange.AddEligible when roleId.HasValue:
                settings.ChangeEligibleRole(roleId.Value, add: true);
                break;
            case WorkingConditionsChange.RemoveEligible when roleId.HasValue:
                settings.ChangeEligibleRole(roleId.Value, add: false);
                break;
            case WorkingConditionsChange.AddExempt when roleId.HasValue:
                settings.ChangeExemptRole(roleId.Value, add: true);
                break;
            case WorkingConditionsChange.RemoveExempt when roleId.HasValue:
                settings.ChangeExemptRole(roleId.Value, add: false);
                break;
            default:
                throw new BotCommandException("Choose a role for this configuration change.");
        }
    }
}