namespace SylvanBot.Features.WorkingHours;

using SylvanBot.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
public sealed class WorkingHoursModule(WorkingHoursCoordinator coordinator, IHostApplicationLifetime lifetime)
    : SlashCommandBase
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

        ErrorOr<string> result = await coordinator.SetScheduleAsync(
            Context.Guild.Id,
            Context.User.Id,
            Context.User.Username,
            enable,
            start,
            end,
            timezone,
            lifetime.ApplicationStopping);

        await WorkingHoursReply.SendAsync(Context.Interaction, result.IsError ? result.FirstError.Description : result.Value);
    }
}