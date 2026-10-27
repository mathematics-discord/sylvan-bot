namespace SylvanBot.Features.RoleColours;

[Group("colour-admin", "Administrative role colour commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class RoleColourAdminModule(RoleColourOperations operations, RoleColourReconciler reconciler)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string ResponseNoGuild = "This command can only be used in a guild.";

    [SlashCommand("upsert", "Create or update a source/display role colour mapping.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task UpsertAsync(
        [Summary("source", "Uncoloured source role granting access.")]
        IRole source,
        [Summary("display", "Display colour role Sylvan Bot manages.")]
        IRole display,
        [Summary("id", "Existing mapping ID to update; omit to create.")]
        long? id = null
    )
    {
        if (Context.Guild is null)
        {
            await RespondAsync(text: ResponseNoGuild, ephemeral: true);

            return;
        }

        ErrorOr<Success> roleValidation = RoleColourReconciler.ValidateMapping(guild: Context.Guild, sourceId: source.Id, colourId: display.Id);

        if (roleValidation.IsError)
        {
            await RespondAsync(text: roleValidation.FirstError.Description, ephemeral: true);

            return;
        }

        await DeferAsync(true);

        ImmutableArray<RoleColourOption> previous =
            await operations.ListConfiguredOptionsAsync(guildId: Context.Guild.Id, ct: CancellationToken.None);

        ErrorOr<(RoleColourOption Option, bool Created)> result = await operations.UpsertMappingAsync(
            guildId: Context.Guild.Id,
            optionId: id,
            sourceRoleId: source.Id,
            displayRoleId: display.Id,
            ct: CancellationToken.None
        );

        if (result.IsError)
        {
            await FollowupAsync(text: result.FirstError.Description, ephemeral: true);

            return;
        }

        var (option, created) = result.Value;
        var incomplete = await reconciler.ReconcileGuildAsync(guild: Context.Guild, ct: CancellationToken.None, previousMappings: previous);
        var action     = created ? "Registered" : "Updated";

        await FollowupAsync(
            $"{action} colour mapping `{option.OptionId}`: {source.Mention} -> {display.Mention}.{CheckSummary(incomplete)}",
            ephemeral: true
        );
    }

    [SlashCommand("delete", "Delete a configured role colour option.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task DeleteAsync([Summary("id", "ID of the colour mapping to delete.")] long id)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(text: ResponseNoGuild, ephemeral: true);

            return;
        }

        await DeferAsync(true);

        ImmutableArray<RoleColourOption> previous =
            await operations.ListConfiguredOptionsAsync(guildId: Context.Guild.Id, ct: CancellationToken.None);

        ErrorOr<RoleColourOption> result =
            await operations.DeleteMappingAsync(guildId: Context.Guild.Id, optionId: id, ct: CancellationToken.None);

        if (result.IsError)
        {
            await FollowupAsync(text: result.FirstError.Description, ephemeral: true);

            return;
        }

        var incomplete = await reconciler.ReconcileGuildAsync(guild: Context.Guild, ct: CancellationToken.None, previousMappings: previous);

        await FollowupAsync(
            $"Deleted colour mapping `{result.Value.OptionId}`.{CheckSummary(incomplete)}",
            ephemeral: true);
    }

    [SlashCommand("sync", "Reconcile display colour roles for all members who currently have a source colour role.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task SyncAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(text: ResponseNoGuild, ephemeral: true);

            return;
        }

        await DeferAsync(true);

        ImmutableArray<RoleColourOption> mappings =
            await operations.ListConfiguredOptionsAsync(guildId: Context.Guild.Id, ct: CancellationToken.None);

        if (mappings.IsEmpty)
        {
            await FollowupAsync("No colour mappings are configured.", ephemeral: true);

            return;
        }

        var incomplete = await reconciler.ReconcileGuildAsync(guild: Context.Guild, ct: CancellationToken.None);
        await FollowupAsync($"Role colour check completed.{CheckSummary(incomplete)}", ephemeral: true);
    }

    private static string CheckSummary(int incomplete) => incomplete == 0 ? string.Empty : $" {incomplete} update(s) incomplete; see logs.";
}