namespace SylvanBot.Features.Configuration;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed partial class ConfigModule
{
    [Group("tickets", "Administrator ticket settings.")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [RequireUserPermission(GuildPermission.Administrator)]
    [RequireContext(ContextType.Guild)]
    public sealed class TicketsModule(TicketWorkflow workflow, TicketWorker worker, ILogger logger, IHostApplicationLifetime lifetime, IDbContextFactory<BotDbContext> contextFactory)
        : InteractionModuleBase<IInteractionContext>
    {
        [SlashCommand("review-exempt", "View, add or remove roles exempt from ticket review.")]
        public async Task ReviewExemptAsync(
            [Summary("add", "Role whose members do not require ticket review.")] IRole? add = null,
            [Summary("remove", "Role to remove from the exemption set.")] IRole? remove = null)
        {
            if (Context.User is not IGuildUser { GuildPermissions.Administrator: true } || Context.Guild is null)
                throw new BotCommandException("Only administrators can configure ticket review.");

            if ((add is not null && add.Guild.Id != Context.Guild.Id)
                || (remove is not null && remove.Guild.Id != Context.Guild.Id))
                throw new BotCommandException("Choose roles from this server.");

            if (add is not null && add.Id == remove?.Id)
                throw new BotCommandException("Choose different roles to add and remove.");

            await DeferAsync(ephemeral: true);
            await using var dbContext = await contextFactory.CreateDbContextAsync(lifetime.ApplicationStopping);
            var configuration = await dbContext.TicketConfigurations
                .SingleOrDefaultAsync(value => value.GuildId == Context.Guild.Id, lifetime.ApplicationStopping);
            var isNew = configuration is null;
            configuration ??= new TicketConfiguration { GuildId = Context.Guild.Id };

            if (add is not null || remove is not null)
            {
                if (add is not null && !configuration.ReviewExemptRoleIds.Contains(checked((long)add.Id)))
                    configuration.ReviewExemptRoleIds.Add(checked((long)add.Id));

                if (remove is not null)
                    configuration.ReviewExemptRoleIds.Remove(checked((long)remove.Id));

                if (isNew)
                    dbContext.TicketConfigurations.Add(configuration);

                await dbContext.SaveChangesAsync(lifetime.ApplicationStopping);
            }

            IEnumerable<string> roles = configuration.ReviewExemptRoleIds
                .Select(id => $"<@&{id}>");
            var description = configuration.ReviewExemptRoleIds.Count == 0 ? "None" : string.Join(", ", roles);
            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = $"Review-exempt roles: {description}\nChanges apply to newly captured tickets. Existing review decisions are retained.";
                properties.AllowedMentions = AllowedMentions.None;
            });
        }

        [SlashCommand("config", "View or update ticket configuration.")]
        public async Task ConfigAsync(
            [Summary("enabled", "Whether the entire ticket system is enabled.")] bool? enabled = null,
            [Summary("channel", "Staff tickets channel.")] [ChannelTypes(ChannelType.Text)] ITextChannel? channel = null,
            [Summary("reminder-minutes", "DM reminder interval in minutes (default 1440).")]
            [MinValue(1)] [MaxValue(35791394)] int? reminderMinutes = null)
        {
            await DeferAsync(ephemeral: true);

            if (Context.User is not IGuildUser { GuildPermissions.Administrator: true } moderator || Context.Guild is null)
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

                throw new BotCommandException(response, exception);
            }
        }
    }
}
