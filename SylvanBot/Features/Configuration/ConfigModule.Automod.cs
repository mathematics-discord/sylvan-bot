using SylvanBot.Application.Features.Automod;
using SylvanBot.Features.Automod;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [SlashCommand("automod", "Enable or disable automod and manage exempt roles")]
    public async Task AutomodAsync(
        [Summary("action", "Enable or disable a rule action, change role exemptions, or list settings")]
        AutomodConfigurationAction action,
        [Summary("role-mention", "The role to exempt or unexempt")]
        IRole? roleMention = null,
        [Summary("rule-action", "The automod action to enable or disable")]
        AutomodAction? ruleAction = null)
    {
        if (Context.User is not SocketGuildUser { GuildPermissions.Administrator: true })
            throw new BotCommandException("Only administrators can use /config.");

        await DeferAsync(ephemeral: false);

        DotNext.Optional<AutomodAction> selectedAction = ruleAction is { } selected
            ? new DotNext.Optional<AutomodAction>(selected)
            : default;

        if (action is AutomodConfigurationAction.Enable or AutomodConfigurationAction.Disable)
        {
            if (roleMention is not null)
                throw new BotCommandException("Enable and Disable do not accept a role. No settings changed.");

            if (selectedAction is not { HasValue: true, Value: var targetAction })
                throw new BotCommandException("Choose Delete, Note, Mute or Ban in rule-action to enable or disable it. No settings changed.");

            var enabled = action == AutomodConfigurationAction.Enable;
            var activated = await automod.SetEnabledAsync(Context.Guild.Id, targetAction, enabled, lifetime.ApplicationStopping);
            var response = enabled
                ? $"Automod {targetAction} enabled. Saved rules and exemptions are retained."
                : $"Automod {targetAction} disabled. Saved rules and exemptions are retained.";

            var storedOnly = targetAction is AutomodAction.Note or AutomodAction.Ban;

            if (enabled && storedOnly)
                response += " Note and Ban actions are stored but are not executed yet.";

            await AutomodReply.SendAsync(Context.Interaction, AutomodReply.WithRefreshStatus(response, activated), ephemeral: false);
            return;
        }

        if (selectedAction.HasValue)
            throw new BotCommandException("rule-action is only used with Enable or Disable. No settings changed.");

        if (action == AutomodConfigurationAction.List)
        {
            ImmutableArray<ulong> ids = await automod.ListExemptRolesAsync(Context.Guild.Id, lifetime.ApplicationStopping);

            ImmutableArray<AutomodAction> disabledActions = await automod.ListDisabledActionsAsync(Context.Guild.Id, lifetime.ApplicationStopping);
            IEnumerable<string> statuses = Enum.GetValues<AutomodAction>()
                .Select(rule => $"{rule}: {(disabledActions.Contains(rule) ? "disabled" : "enabled")}");

            IEnumerable<string> rows = ids
                .Select(id => Context.Guild.GetRole(id) is { } role ? $"{role.Name} ({id})" : $"Deleted role ({id})");

            var exemptions = ids.IsEmpty ? "No automod exempt roles in this guild." : "Exempt roles:\n" + string.Join("\n", rows);
            var response = string.Join("\n", statuses) + "\nNote and Ban actions are stored but are not executed yet.\n\n" + exemptions;

            await AutomodReply.SendAsync(
                Context.Interaction,
                response,
                ephemeral: false);

            return;
        }

        if (action is not (AutomodConfigurationAction.Exempt or AutomodConfigurationAction.Unexempt))
            throw new BotCommandException("Choose Enable, Disable, Exempt, Unexempt or List.");

        if (roleMention is null || roleMention.Guild.Id != Context.Guild.Id)
            throw new BotCommandException("Choose a role from this guild to exempt or unexempt. No exemptions changed.");

        var exempt = action == AutomodConfigurationAction.Exempt;
        var refreshed = await automod.SetExemptAsync(Context.Guild.Id, roleMention.Id, exempt, lifetime.ApplicationStopping);

        await ModifyOriginalResponseAsync(properties =>
        {
            var response = exempt
                ? $"Role {roleMention.Id} is exempt from automod."
                : $"Role {roleMention.Id} is not exempt from automod.";

            properties.Content = AutomodReply.WithRefreshStatus(response, refreshed);
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}
