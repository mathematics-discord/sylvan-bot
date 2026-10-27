namespace SylvanBot.Features.SylvanMail;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Interactions;

[Group("config", "Administrator-only bot configuration")]
[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireUserPermission(GuildPermission.Administrator)]
public sealed class ConfigModule(SylvanMailWorkflow workflow, IHostApplicationLifetime lifetime) : SlashCommandBase
{
    [SlashCommand("sylvanmail", "Enable private SylvanMail in a forum and select its ping role")]
    public async Task SylvanMailAsync(
        [Summary("enable", "The private forum for SylvanMail")]
        [ChannelTypes(ChannelType.Forum)] IChannel enable,
        [Summary("role", "The role to ping for new cases and after an inbound gap of three hours")] IRole role)
    {
        // Keep the whole group protected by its runtime precondition, and check at this write boundary too.
        if (Context.User is not SocketGuildUser { GuildPermissions.Administrator: true })
            throw new BotCommandException("Only administrators can use /config.");

        await DeferAsync(ephemeral: true);

        if (enable is not IForumChannel forum || forum.GuildId != Context.Guild.Id || role.Guild.Id != Context.Guild.Id)
            throw new BotCommandException("Choose a forum and a ping role from this guild. No settings changed.");

        await workflow.ConfigureAsync(Context.Guild.Id, forum.Id, role.Id, lifetime.ApplicationStopping);

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = $"SylvanMail enabled in <#{forum.Id}> for <@&{role.Id}>. Changes apply immediately; previous posts and mappings are retained.";
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}
