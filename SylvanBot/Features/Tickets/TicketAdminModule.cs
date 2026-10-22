namespace SylvanBot.Features.Tickets;

[Group("ticket-admin", "Administrator ticket settings.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireUserPermission(GuildPermission.Administrator)]
[RequireContext(ContextType.Guild)]
public sealed class TicketAdminModule(TicketWorkflow workflow, ILogger logger, IHostApplicationLifetime lifetime)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("config", "View or update ticket configuration.")]
    public async Task ConfigAsync(
        [Summary("enabled", "Whether creation of notes is enabled.")] bool? enabled = null,
        [Summary("channel", "Staff tickets channel.")] [ChannelTypes(ChannelType.Text)] ITextChannel? channel = null)
    {
        await DeferAsync(ephemeral: true);
        string response;

        try
        {
            if (Context.User is not IGuildUser moderator || Context.Guild is null)
                throw new UnauthorizedAccessException();

            var actor = new TicketActor(Context.Guild.Id, moderator.Id,
                moderator.GuildPermissions.Administrator, moderator.GuildPermissions.ModerateMembers);

            var configuration = await workflow.ConfigureAsync(actor, enabled, channel?.Id, lifetime.ApplicationStopping);
            var destination = configuration.DestinationChannelId is null
                ? "unset"
                : $"<#{configuration.DestinationChannelId}>";

            response = $"Note creation: {(configuration.Enabled ? "enabled" : "disabled")}\nDestination: {destination}";
        }
        catch (Exception exception)
        {
            response = exception is UnauthorizedAccessException
                ? "Administrator permission and access to the selected channel are required."
                : "Configuration could not be updated. Enabling notes or selecting a channel requires "
                    + "a destination where Sylvan can view, read history, send messages, embed links and attach files.";

            logger.Warning("Ticket configuration failed in guild {GuildId}: {FailureType}",
                Context.Guild?.Id, exception.GetType().Name);
        }

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = response;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}