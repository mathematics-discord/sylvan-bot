namespace SylvanBot.Features.Configuration;

using SylvanBot.Application.Features.Tickets;

public sealed partial class ConfigModule
{
    [SlashCommand("tickets", "View or update ticket configuration.")]
    public async Task TicketsAsync(
        [Summary("enabled", "Whether the ticket system is enabled.")] bool? enabled = null,
        [Summary("channel", "Staff tickets channel.")] [ChannelTypes(ChannelType.Text)] ITextChannel? channel = null,
        [Summary("reminder-minutes", "DM reminder interval in minutes (default 1440).")] [MinValue(1)] [MaxValue(35791394)] int? reminderMinutes =
            null,
        [Summary("exempt-role-add", "Add a role exempt from ticket review.")] IRole? add = null,
        [Summary("exempt-role-remove", "Remove a role from the review exemption set.")] IRole? remove = null,
        [Summary("tracked-role-add", "Create tickets when this role is granted.")] IRole? trackedAdd = null,
        [Summary("tracked-role-remove", "Stop creating new tickets for this role.")] IRole? trackedRemove = null)
    {
        await DeferAsync(ephemeral: true);

        if (Context.User is not IGuildUser { GuildPermissions.Administrator: true } moderator || Context.Guild is null)
            throw new BotCommandException("Only administrators can configure tickets.");

        var addRoleNotExists = add is not null && add.Guild.Id != Context.Guild.Id;
        var removeRoleNotExists = remove is not null && remove.Guild.Id != Context.Guild.Id;
        var trackedAddRoleNotExists = trackedAdd is not null && trackedAdd.Guild.Id != Context.Guild.Id;
        var trackedRemoveRoleNotExists = trackedRemove is not null && trackedRemove.Guild.Id != Context.Guild.Id;

        if (addRoleNotExists || removeRoleNotExists || trackedAddRoleNotExists || trackedRemoveRoleNotExists)
            throw new BotCommandException("Choose roles from this server.");

        if (add is not null && add.Id == remove?.Id)
            throw new BotCommandException("Choose different roles to add and remove.");

        if (trackedAdd is not null && (trackedAdd.Id == trackedRemove?.Id || trackedAdd.Id == Context.Guild.Id || trackedAdd.IsManaged))
            throw new BotCommandException("Choose distinct ordinary roles to track and untrack.");

        var actor = new TicketActor(Context.Guild.Id, moderator.Id, true, moderator.GuildPermissions.ModerateMembers);

        var configuration = await ticketCoordinator.ConfigureAsync(
            actor,
            enabled,
            channel?.Id,
            reminderMinutes * 60,
            lifetime.ApplicationStopping,
            add?.Id,
            remove?.Id,
            trackedAdd?.Id,
            trackedRemove?.Id);

        ticketWorker.Wake();
        var destination = configuration.DestinationChannelId is null ? "unset" : $"<#{configuration.DestinationChannelId}>";

        IEnumerable<string> roles = configuration.ReviewExemptRoleIds.Select(id => $"<@&{id}>");
        var exemptions = configuration.ReviewExemptRoleIds.Count == 0 ? "None" : string.Join(", ", roles);
        IEnumerable<string> trackedRoles = configuration.TrackedRoleIds.Select(id => $"<@&{id}>");
        var tracked = configuration.TrackedRoleIds.Count == 0 ? "None" : string.Join(", ", trackedRoles);

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = $"Tickets: {(configuration.Enabled ? "enabled" : "disabled")}\nDestination: {destination}\n"
                                 + $"DM reminder interval: {configuration.ReminderIntervalSeconds / 60} minutes\nReview-exempt roles: {exemptions}\n"
                                 + $"Tracked roles: {tracked}\n"
                                 + "Exemption changes apply to newly captured tickets.";

            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}