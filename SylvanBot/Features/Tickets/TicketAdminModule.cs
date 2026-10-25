namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

[Group("ticket-admin", "Administrator ticket settings.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireUserPermission(GuildPermission.Administrator)]
[RequireContext(ContextType.Guild)]
public sealed class TicketAdminModule(TicketWorkflow workflow, TicketWorker worker, ILogger logger, IHostApplicationLifetime lifetime)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("config", "View or update ticket configuration.")]
    public async Task ConfigAsync(
        [Summary("enabled", "Whether the entire ticket system is enabled.")] bool? enabled = null,
        [Summary("channel", "Staff tickets channel.")] [ChannelTypes(ChannelType.Text)] ITextChannel? channel = null,
        [Summary("reminder-minutes", "DM reminder interval in minutes (default 1440).")]
        [MinValue(1)] [MaxValue(35791394)] int? reminderMinutes = null)
    {
        await DeferAsync(ephemeral: true);

        if (Context.User is not IGuildUser moderator || Context.Guild is null)
            throw new BotCommandException("Use this command in a server.");

        var actor = new TicketActor(
            Context.Guild.Id,
            moderator.Id,
            moderator.GuildPermissions.Administrator,
            moderator.GuildPermissions.ModerateMembers);

        var configuration = await workflow.ConfigureAsync(actor, enabled, channel?.Id,
            reminderMinutes * 60, lifetime.ApplicationStopping);
        worker.Wake();
        var destination = configuration.DestinationChannelId is null
            ? "unset"
            : $"<#{configuration.DestinationChannelId}>";

        var response = $"Tickets: {(configuration.Enabled ? "enabled" : "disabled")}\nDestination: {destination}\n"
                       + $"DM reminder interval: {configuration.ReminderIntervalSeconds / 60} minutes\n"
                       + "Disabling tickets pauses capture, reminders and scheduled expiry. Stored tickets are retained.";

        try
        {
            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = response;
                properties.AllowedMentions = AllowedMentions.None;
            });
        }
        catch (Exception exception)
        {
            logger.Warning(
                "Ticket configuration in guild {GuildId}: delivering the confirmed configuration failed ({FailureType})",
                actor.GuildId,
                exception.GetType().Name);

            throw new BotCommandException(response);
        }
    }
}
