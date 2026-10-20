namespace RatBot.Features.RoleColours;

using System.Text;

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
        [Summary("display", "Display colour role RatBot manages.")]
        IRole display,
        [Summary("id", "Existing mapping ID to update; omit to create.")]
        long? id = null
    )
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        ErrorOr<Success> roleValidation = RoleColourReconciler.ValidateMapping(Context.Guild, source.Id, display.Id);

        if (roleValidation.IsError)
        {
            await RespondAsync(roleValidation.FirstError.Description, ephemeral: true);
            return;
        }

        await DeferAsync(true);

        ImmutableArray<RoleColourOption> previous =
            await operations.ListConfiguredOptionsAsync(Context.Guild.Id, CancellationToken.None);

        ErrorOr<(RoleColourOption Option, bool Created)> result = await operations.UpsertMappingAsync(
            Context.Guild.Id,
            id,
            source.Id,
            display.Id,
            CancellationToken.None
        );

        if (result.IsError)
        {
            await FollowupAsync(result.FirstError.Description, ephemeral: true);
            return;
        }

        (var option, var created) = result.Value;
        var incomplete = await reconciler.ReconcileGuildAsync(Context.Guild, CancellationToken.None, previous);
        var action = created ? "Registered" : "Updated";
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
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        await DeferAsync(true);

        ImmutableArray<RoleColourOption> previous =
            await operations.ListConfiguredOptionsAsync(Context.Guild.Id, CancellationToken.None);

        ErrorOr<RoleColourOption> result =
            await operations.DeleteMappingAsync(Context.Guild.Id, id, CancellationToken.None);

        if (result.IsError)
        {
            await FollowupAsync(result.FirstError.Description, ephemeral: true);
            return;
        }

        var incomplete = await reconciler.ReconcileGuildAsync(Context.Guild, CancellationToken.None, previous);
        await FollowupAsync($"Deleted colour mapping `{result.Value.OptionId}`.{CheckSummary(incomplete)}",
            ephemeral: true);
    }

    [SlashCommand("list", "List configured role colour options.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ListAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        ImmutableArray<RoleColourOption> options =
            await operations.ListConfiguredOptionsAsync(Context.Guild.Id, CancellationToken.None);

        if (options.IsEmpty)
        {
            await RespondAsync("No colour options are configured.", ephemeral: true);
            return;
        }

        await RespondAsync(BuildListResponse(options), ephemeral: true);
    }

    [SlashCommand("sync", "Reconcile display colour roles for all members who currently have a source colour role.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task SyncAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        await DeferAsync(true);

        ImmutableArray<RoleColourOption> mappings =
            await operations.ListConfiguredOptionsAsync(Context.Guild.Id, CancellationToken.None);

        if (mappings.IsEmpty)
        {
            await FollowupAsync("No colour mappings are configured.", ephemeral: true);
            return;
        }

        var incomplete = await reconciler.ReconcileGuildAsync(Context.Guild, CancellationToken.None);
        await FollowupAsync($"Role colour check completed.{CheckSummary(incomplete)}", ephemeral: true);
    }

    private static string CheckSummary(int incomplete)
    {
        return incomplete == 0 ? string.Empty : $" {incomplete} update(s) incomplete; see logs.";
    }

    private static string BuildListResponse(IReadOnlyList<RoleColourOption> options)
    {
        var builder = new StringBuilder("Configured colour options:");

        foreach (var option in options)
            builder.AppendLine()
                .Append($"`{option.OptionId}`: <@&{option.SourceRoleId}> -> <@&{option.DisplayRoleId}>");

        return builder.ToString();
    }
}