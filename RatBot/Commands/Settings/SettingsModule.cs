using RatBot.Infrastructure.Features.Meta;

namespace RatBot.Commands.Settings;

[Group("config", "Configuration commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class SettingsModule : SlashCommandBase
{
    // ReSharper disable InconsistentNaming
    private const string RESPONSE_NO_GUILD = "This command can only be used in a guild.";

    // ReSharper enable InconsistentNaming

    [Group("meta", "Meta configuration.")]
    public sealed class MetaSettingsModule(MetaSuggestionSettingsService metaSuggestionSettingsService)
        : SlashCommandBase
    {
        [SlashCommand("suggestions", "Set the forum channel used for suggestion threads.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetSuggestionsForumChannelAsync(IForumChannel channel)
        {
            if (Context.Guild is null)
            {
                await RespondAsync(RESPONSE_NO_GUILD, ephemeral: true);
                return;
            }

            ErrorOr<Success> result =
                await metaSuggestionSettingsService.UpsertSuggestionsForumChannelAsync(Context.Guild.Id, channel.Id);

            await result.SwitchFirstAsync(
                async _ => await RespondAsync($"Meta suggestions forum set to {channel.Mention}.", ephemeral: true),
                async error => await RespondAsync(error.Description, ephemeral: true)
            );
        }

        [SlashCommand("proposals", "Set the forum channel used for successful proposals.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetProposalsForumChannelAsync(IForumChannel channel)
        {
            if (Context.Guild is null)
            {
                await RespondAsync(RESPONSE_NO_GUILD, ephemeral: true);
                return;
            }

            ErrorOr<Success> result =
                await metaSuggestionSettingsService.UpsertProposalsForumChannelAsync(Context.Guild.Id, channel.Id);

            await result.SwitchFirstAsync(
                async _ => await RespondAsync($"Meta proposals forum set to {channel.Mention}.", ephemeral: true),
                async error => await RespondAsync(error.Description, ephemeral: true)
            );
        }

        [SlashCommand("cabinet", "Set the Cabinet role.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetCabinetRoleAsync(IRole role)
        {
            if (Context.Guild is null)
            {
                await RespondAsync(RESPONSE_NO_GUILD, ephemeral: true);
                return;
            }

            ErrorOr<Success> result =
                await metaSuggestionSettingsService.UpsertCabinetRoleAsync(Context.Guild.Id, role.Id);

            await result.SwitchFirstAsync(
                async _ => await RespondAsync($"Meta Cabinet role set to {role.Mention}.", ephemeral: true),
                async error => await RespondAsync(error.Description, ephemeral: true)
            );
        }

        [SlashCommand("chair", "Set the Cabinet Chair role.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetCabinetChairRoleAsync(IRole role)
        {
            if (Context.Guild is null)
            {
                await RespondAsync(RESPONSE_NO_GUILD, ephemeral: true);
                return;
            }

            ErrorOr<Success> result =
                await metaSuggestionSettingsService.UpsertCabinetChairRoleAsync(Context.Guild.Id, role.Id);

            await result.SwitchFirstAsync(
                async _ => await RespondAsync($"Meta Cabinet Chair role set to {role.Mention}.", ephemeral: true),
                async error => await RespondAsync(error.Description, ephemeral: true)
            );
        }

        [SlashCommand("committee", "Set the Committee role.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetCommitteeRoleAsync(IRole role)
        {
            if (Context.Guild is null)
            {
                await RespondAsync(RESPONSE_NO_GUILD, ephemeral: true);
                return;
            }

            ErrorOr<Success> result =
                await metaSuggestionSettingsService.UpsertCommitteeRoleAsync(Context.Guild.Id, role.Id);

            await result.SwitchFirstAsync(
                async _ => await RespondAsync($"Meta Committee role set to {role.Mention}.", ephemeral: true),
                async error => await RespondAsync(error.Description, ephemeral: true)
            );
        }
    }
}