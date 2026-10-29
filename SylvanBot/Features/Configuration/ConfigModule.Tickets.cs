namespace SylvanBot.Features.Configuration;

using SylvanBot.Application.Features.Tickets;

public sealed partial class ConfigModule
{
    [SlashCommand("tickets", "View or update ticket configuration.")]
    public async Task TicketsAsync(
        [Summary("enabled", "Whether the ticket system is enabled.")] bool? enabled = null,
        [Summary("channel", "Staff tickets channel.")] [ChannelTypes(ChannelType.Text)] ITextChannel? channel = null,
        [Summary("reminder-minutes", "DM reminder interval in minutes (default 1440).")]
        [MinValue(1)] [MaxValue(35791394)] int? reminderMinutes = null,
        [Summary("exempt-role-add", "Add a role exempt from ticket review.")] IRole? add = null,
        [Summary("exempt-role-remove", "Remove a role from the review exemption set.")] IRole? remove = null)
    {
        await DeferAsync(ephemeral: true);

        if (Context.User is not IGuildUser { GuildPermissions.Administrator: true } moderator || Context.Guild is null)
            throw new BotCommandException("Only administrators can configure tickets.");

        if ((add is not null && add.Guild.Id != Context.Guild.Id)
            || (remove is not null && remove.Guild.Id != Context.Guild.Id))
            throw new BotCommandException("Choose roles from this server.");

        if (add is not null && add.Id == remove?.Id)
            throw new BotCommandException("Choose different roles to add and remove.");

        var actor = new TicketActor(Context.Guild.Id, moderator.Id, true, moderator.GuildPermissions.ModerateMembers);
        var configuration = await ticketWorkflow.ConfigureAsync(actor, enabled, channel?.Id,
            reminderMinutes * 60, lifetime.ApplicationStopping, add?.Id, remove?.Id);
        ticketWorker.Wake();
        var destination = configuration.DestinationChannelId is null ? "unset" : $"<#{configuration.DestinationChannelId}>";
        IEnumerable<string> roles = configuration.ReviewExemptRoleIds
            .Select(id => $"<@&{id}>");
        var exemptions = configuration.ReviewExemptRoleIds.Count == 0 ? "None" : string.Join(", ", roles);
        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = $"Tickets: {(configuration.Enabled ? "enabled" : "disabled")}\nDestination: {destination}\n"
                + $"DM reminder interval: {configuration.ReminderIntervalSeconds / 60} minutes\nReview-exempt roles: {exemptions}\n"
                + "Exemption changes apply to newly captured tickets.";
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}
