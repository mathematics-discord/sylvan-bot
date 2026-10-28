using System.Text;

namespace SylvanBot.Features.RoleColours;

[Group("colour", "Choose or list display colours.")]
public sealed class ColourModule(RoleColourReconciler colours, RoleColourOperations operations) : InteractionModuleBase<IInteractionContext>
{
    private const string SwapPrefix = "colour-swap";

    [ComponentInteraction($"{SwapPrefix}:select:*", ignoreGroupNames: true)]
    public async Task OnSwapSelectAsync(ulong ownerUserId, string[] values)
    {
        if (!await CanUseMenuAsync(ownerUserId))
            return;

        if (values.Length != 1 || !ulong.TryParse(values[0], out var selected))
        {
            await RespondAsync("Invalid selection.", ephemeral: true);

            return;
        }

        await ApplySelectionAsync(selected);
    }

    [ComponentInteraction($"{SwapPrefix}:reset:*", ignoreGroupNames: true)]
    public async Task OnResetAsync(ulong ownerUserId)
    {
        if (!await CanUseMenuAsync(ownerUserId))
            return;

        await ApplySelectionAsync(null);
    }

    [SlashCommand("set", "Choose or reset your display colour.")]
    public async Task ColourAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);

            return;
        }

        await DeferAsync(true);

        ImmutableArray<IRole> eligible = await colours.ListEligibleColoursAsync(
            guild: Context.Guild,
            userId: Context.User.Id,
            ct: CancellationToken.None
        );

        if (eligible.Length > 25)
        {
            await FollowupAsync(
                "You have too many eligible colours for one menu. Contact a server administrator. You can still reset your colour.",
                components: BuildMenu(ownerUserId: Context.User.Id, []),
                ephemeral: true
            );

            return;
        }

        var prompt = eligible.IsEmpty
            ? "You do not currently have any colours to select. Use Reset to remove your display colour."
            : "Select a colour to apply it, or use Reset to remove your display colour.";

        await FollowupAsync(text: prompt, components: BuildMenu(ownerUserId: Context.User.Id, eligible: eligible), ephemeral: true);
    }

    [SlashCommand("list", "List configured role colour options.")]
    public async Task ListAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);

            return;
        }

        ImmutableArray<RoleColourOption> options = await operations.ListConfiguredOptionsAsync(guildId: Context.Guild.Id, ct: CancellationToken.None);

        if (options.IsEmpty)
        {
            await RespondAsync("No colour options are configured.", ephemeral: true);

            return;
        }

        await RespondAsync(BuildListResponse(options), ephemeral: true);
    }

    internal static MessageComponent BuildMenu(ulong ownerUserId, ImmutableArray<IRole> eligible)
    {
        var components = new ComponentBuilder();

        if (!eligible.IsEmpty)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"{SwapPrefix}:select:{ownerUserId}")
                .WithPlaceholder("Choose a colour…")
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (var role in eligible)
                menu.AddOption(label: role.Name, role.Id.ToString());

            components.WithSelectMenu(menu);
        }

        return components.WithButton("Reset", $"{SwapPrefix}:reset:{ownerUserId}", style: ButtonStyle.Danger).Build();
    }

    private static string ResultMessage(ErrorOr<Option<ulong>> result)
    {
        if (result.IsError)
            return result.FirstError.Description;

        if (result.Value.Value is Some<ulong>(var colourId))
            return $"You are now wearing <@&{colourId}>.";

        return "Your display colour has been removed.";
    }

    private static string BuildListResponse(IReadOnlyList<RoleColourOption> options)
    {
        var builder = new StringBuilder("Configured colour options:");

        foreach (var option in options)
            builder.AppendLine().Append($"`{option.OptionId}`: <@&{option.SourceRoleId}> -> <@&{option.DisplayRoleId}>");

        return builder.ToString();
    }

    private async Task ApplySelectionAsync(ulong? selected)
    {
        await DeferAsync(true);

        ErrorOr<Option<ulong>> result = await colours.TrySetUserColourAsync(
            guild: Context.Guild,
            userId: Context.User.Id,
            colourId: selected,
            ct: CancellationToken.None
        );

        await FollowupAsync(ResultMessage(result), ephemeral: true);

        if (Context.Interaction is SocketMessageComponent component)
            await component.ModifyOriginalResponseAsync(message => message.Components = new ComponentBuilder().Build());
    }

    private async Task<bool> CanUseMenuAsync(ulong ownerUserId)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);

            return false;
        }

        if (Context.User.Id != ownerUserId)
        {
            await RespondAsync("This colour selection menu is not for you.", ephemeral: true);

            return false;
        }

        return true;
    }
}
