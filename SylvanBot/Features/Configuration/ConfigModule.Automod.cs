using SylvanBot.Application.Features.Automod;
using SylvanBot.Features.Automod;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [Group("automod", "Configure automod actions and role exemptions")]
    [CommandContextType(InteractionContextType.Guild)]
    [RequireContext(ContextType.Guild)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public sealed class AutomodConfigModule(AutomodManagement automod, IHostApplicationLifetime lifetime) : SlashCommandBase
    {
        [SlashCommand("set", "Enable or disable one automod action, or all actions")]
        public async Task SetAsync(
            bool enabled,
            [Summary("action", "Delete, Note, Mute or Ban; omit to update all four")]
            AutomodAction? action = null)
        {
            RequireAdministrator();
            await DeferAsync(ephemeral: false);
            DotNext.Optional<AutomodAction> selectedAction = action is { } selected
                ? new DotNext.Optional<AutomodAction>(selected)
                : default;

            var refreshed = await automod.SetEnabledAsync(Context.Guild.Id, selectedAction, enabled, lifetime.ApplicationStopping);
            var target = selectedAction.HasValue ? selectedAction.Value.ToString() : "all actions";
            var state = enabled ? "enabled" : "disabled";
            var response = $"Automod {target} {state}. Saved rules and exemptions are retained.";
            var includesStoredOnly = !selectedAction.HasValue || selectedAction.Value is AutomodAction.Note or AutomodAction.Ban;

            if (enabled && includesStoredOnly)
                response += " Note and Ban actions are stored but are not executed yet.";

            await AutomodReply.SendAsync(Context.Interaction, AutomodReply.WithRefreshStatus(response, refreshed), ephemeral: false);
        }

        [SlashCommand("exempt", "Add or remove a role's automod exemption")]
        public async Task ExemptAsync(IRole role, bool enabled)
        {
            RequireAdministrator();

            if (role.Guild.Id != Context.Guild.Id)
                throw new BotCommandException("Choose a role from this guild. No exemptions changed.");

            await DeferAsync(ephemeral: false);
            var refreshed = await automod.SetExemptAsync(Context.Guild.Id, role.Id, enabled, lifetime.ApplicationStopping);
            var response = enabled
                ? $"Role {role.Id} is exempt from automod."
                : $"Role {role.Id} is not exempt from automod.";

            await AutomodReply.SendAsync(Context.Interaction, AutomodReply.WithRefreshStatus(response, refreshed), ephemeral: false);
        }

        [SlashCommand("show", "Show automod action settings and exempt roles")]
        public async Task ShowAsync()
        {
            RequireAdministrator();
            await DeferAsync(ephemeral: false);
            ImmutableArray<AutomodAction> disabledActions = await automod.ListDisabledActionsAsync(Context.Guild.Id, lifetime.ApplicationStopping);
            ImmutableArray<ulong> ids = await automod.ListExemptRolesAsync(Context.Guild.Id, lifetime.ApplicationStopping);
            IEnumerable<string> statuses = Enum.GetValues<AutomodAction>()
                .Select(action => $"{action}: {(disabledActions.Contains(action) ? "disabled" : "enabled")}");

            IEnumerable<string> rows = ids
                .Select(id => Context.Guild.GetRole(id) is { } role ? $"{role.Name} ({id})" : $"Deleted role ({id})");

            var exemptions = ids.IsEmpty ? "No automod exempt roles in this guild." : "Exempt roles:\n" + string.Join("\n", rows);
            var response = string.Join("\n", statuses) + "\nNote and Ban actions are stored but are not executed yet.\n\n" + exemptions;

            await AutomodReply.SendAsync(Context.Interaction, response, ephemeral: false);
        }

        private void RequireAdministrator()
        {
            if (Context.User is not SocketGuildUser { GuildPermissions.Administrator: true })
                throw new BotCommandException("Only administrators can use /config.");
        }
    }
}
