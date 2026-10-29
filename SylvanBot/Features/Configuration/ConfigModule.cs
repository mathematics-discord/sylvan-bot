using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Features.Moderation;
using SylvanBot.Features.RoleReactions;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Configuration;

[Group("config", "Administrator-only bot configuration")]
[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireUserPermission(GuildPermission.Administrator)]
public sealed partial class ConfigModule(
    SylvanMailConfiguration workflow,
    RoleReactionConfiguration roleReactions,
    ImageSpamSettingsService settingsService,
    IHostApplicationLifetime lifetime,
    SylvanBot.Application.Features.Tickets.TicketWorkflow ticketWorkflow,
    SylvanBot.Features.Tickets.TicketWorker ticketWorker
) : SlashCommandBase
{
    [SlashCommand("sylvanmail", "Enable private SylvanMail in a forum and select its ping role")]
    public async Task SylvanMailAsync(
        [Summary("enable", "The private forum for SylvanMail")] [ChannelTypes(ChannelType.Forum)]
        IChannel enable,
        [Summary("role", "The role to ping for new cases and after an inbound gap of three hours")]
        IRole role)
    {
        // Keep the whole group protected by its runtime precondition, and check at this write boundary too.
        if (Context.User is not SocketGuildUser { GuildPermissions.Administrator: true })
            throw new BotCommandException("Only administrators can use /config.");

        await DeferAsync(ephemeral: true);

        if (enable is not IForumChannel forum || forum.GuildId != Context.Guild.Id || role.Guild.Id != Context.Guild.Id)
            throw new BotCommandException("Choose a forum and a ping role from this guild. No settings changed.");

        await workflow.ConfigureAsync(guildId: Context.Guild.Id, forumId: forum.Id, roleId: role.Id, ct: lifetime.ApplicationStopping);

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content =
                $"SylvanMail enabled in <#{forum.Id}> for <@&{role.Id}>. Changes apply immediately; previous posts and mappings are retained.";
            properties.AllowedMentions = AllowedMentions.None;
        });
    }

    [SlashCommand("rolereact", "Add or remove a message reaction that grants a role")]
    public async Task RoleReactAsync(
        [Summary("action", "Add or remove a reaction role mapping")]
        RoleReactionAction action,
        [Summary("message_link", "A link to a message in this server")]
        string messageLink,
        [Summary("emoji", "One Unicode emoji or custom emoji")]
        string emoji,
        [Summary("role_mention", "Required for add. Roles with staff permissions are forbidden")]
        IRole? roleMention = null)
    {
        if (Context.User is not SocketGuildUser { GuildPermissions.Administrator: true } administrator)
            throw new BotCommandException("Only administrators can use /config.");

        if (action == RoleReactionAction.Add && roleMention is null)
            throw new BotCommandException("Choose a role when adding a reaction role mapping.");

        await DeferAsync(ephemeral: true);

        var response = (action, roleMention) switch
        {
            (RoleReactionAction.Add, { } targetRole) => await roleReactions.AddAsync(
                guild: Context.Guild,
                moderator: administrator,
                messageLink: messageLink,
                roleId: targetRole.Id,
                emoji: emoji,
                ct: lifetime.ApplicationStopping),
            (RoleReactionAction.Remove, _) => await roleReactions.RemoveAsync(
                guild: Context.Guild,
                messageLink: messageLink,
                emoji: emoji,
                ct: lifetime.ApplicationStopping),
            _ => throw new BotCommandException("Choose add or remove."),
        };

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = response;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}