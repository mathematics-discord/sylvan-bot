using SylvanBot.Interactions;

namespace SylvanBot.Features.WorkingHours;

[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.MuteMembers)]
[RequireUserPermission(GuildPermission.MuteMembers)]
public sealed class WorkingHoursModule(WorkingHoursSchedules coordinator, IHostApplicationLifetime lifetime) : SlashCommandBase
{
    [SlashCommand("working-hours", "Set your recurring daily Working Hours")]
    public async Task SetWorkingHoursAsync(
        bool enable,
        string? start = null,
        string? end = null,
        [Autocomplete(typeof(WorkingHoursTimezoneAutocomplete))]
        string? timezone = null)
    {
        await DeferAsync(ephemeral: true);

        Result<string> result = await coordinator.SetScheduleAsync(
            guildId: Context.Guild.Id,
            memberId: Context.User.Id,
            username: Context.User.Username,
            enable: enable,
            start: start,
            end: end,
            timezone: timezone,
            ct: lifetime.ApplicationStopping);
        await WorkingHoursReply.SendAsync(interaction: Context.Interaction, !result.IsSuccessful ? result.Error!.Message : result.Value);
    }
}