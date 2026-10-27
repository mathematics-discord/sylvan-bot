using SylvanBot.Interactions;

namespace SylvanBot.Features.WorkingHours;

[Group("working-conditions", "Administer Working Hours for this guild")]
[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireUserPermission(GuildPermission.Administrator)]
public sealed class WorkingConditionsModule(WorkingHoursCoordinator coordinator, IHostApplicationLifetime lifetime) : SlashCommandBase
{
    [SlashCommand("show", "Show Working Hours settings and configuration problems")]
    public async Task ShowAsync()
    {
        await DeferAsync(ephemeral: true);
        ErrorOr<string> message = await coordinator.ShowAsync(guildId: Context.Guild.Id, ct: lifetime.ApplicationStopping);

        await WorkingHoursReply.SendAsync(
            interaction: Context.Interaction,
            message.IsError
                ? message.FirstError.Description
                : message.Value);
    }

    [SlashCommand("ping-role", "Set the ping role while scheduling is disabled; the old role is left unchanged")]
    public Task PingRoleAsync(IRole role) => UpdateAsync(change: WorkingConditionsChange.PingRole, role: role);

    [SlashCommand("eligible-role", "Add or remove a role that authorises participation")]
    public Task EligibleRoleAsync(WorkingRoleAction action, IRole role) =>
        UpdateAsync(
            action == WorkingRoleAction.Add
                ? WorkingConditionsChange.AddEligible
                : WorkingConditionsChange.RemoveEligible,
            role: role);

    [SlashCommand("exempt-role", "Add or remove an eligible role's unscheduled exemption")]
    public Task ExemptRoleAsync(WorkingRoleAction action, IRole role) =>
        UpdateAsync(
            action == WorkingRoleAction.Add
                ? WorkingConditionsChange.AddExempt
                : WorkingConditionsChange.RemoveExempt,
            role: role);

    [SlashCommand("enable", "Enable scheduling once configuration is complete and valid")]
    public Task EnableAsync() => UpdateAsync(WorkingConditionsChange.Enable);

    [SlashCommand("disable", "Suspend scheduling and restore ordinary staff ping-role defaults")]
    public Task DisableAsync() => UpdateAsync(WorkingConditionsChange.Disable);

    private async Task UpdateAsync(WorkingConditionsChange change, IRole? role = null)
    {
        await DeferAsync(ephemeral: true);

        if (role is not null && role.Guild.Id != Context.Guild.Id)
        {
            await WorkingHoursReply.SendAsync(interaction: Context.Interaction, "Choose a role from this guild. No settings changed.");

            return;
        }

        ErrorOr<string> result = await coordinator.UpdateAsync(guildId: Context.Guild.Id, change: change, roleId: role?.Id, ct: lifetime.ApplicationStopping);
        await WorkingHoursReply.SendAsync(interaction: Context.Interaction, result.IsError ? result.FirstError.Description : result.Value);
    }
}