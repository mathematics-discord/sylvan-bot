namespace RatBot.Features.RoleColours;

public sealed class ColourModule(RoleColourReconciler colours, ILogger logger) : InteractionModuleBase<IInteractionContext>
{
    private const string SwapPrefix = "colour-swap";

    [ComponentInteraction($"{SwapPrefix}:select:*", true)]
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

    [ComponentInteraction($"{SwapPrefix}:reset:*", true)]
    public async Task OnResetAsync(ulong ownerUserId)
    {
        if (!await CanUseMenuAsync(ownerUserId))
            return;

        await ApplySelectionAsync(null);
    }

    [SlashCommand("colour", "Choose or reset your display colour.")]
    public async Task ColourAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);
            return;
        }

        await DeferAsync(true);

        ImmutableArray<IRole> eligible = await colours.ListEligibleColoursAsync(
            Context.Guild,
            Context.User.Id,
            CancellationToken.None
        );

        if (eligible.Length > 25)
        {
            await FollowupAsync(
                "You have too many eligible colours for one menu. Contact a server administrator. You can still reset your colour.",
                components: BuildMenu(Context.User.Id, []),
                ephemeral: true
            );

            return;
        }

        var prompt = eligible.IsEmpty
            ? "You do not currently have any colours to select. Use Reset to remove your display colour."
            : "Select a colour to apply it, or use Reset to remove your display colour.";

        await FollowupAsync(prompt, components: BuildMenu(Context.User.Id, eligible), ephemeral: true);
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
                menu.AddOption(role.Name, role.Id.ToString());

            components.WithSelectMenu(menu);
        }

        return components.WithButton("Reset", $"{SwapPrefix}:reset:{ownerUserId}", ButtonStyle.Danger).Build();
    }

    private async Task ApplySelectionAsync(ulong? selected)
    {
        await DeferAsync(true);

        string response;
        try
        {
            Option<ulong> colour = await colours.TrySetUserColourAsync(
                Context.Guild, Context.User.Id, selected, CancellationToken.None);

            response = colour.Value is Some<ulong>(var colourId)
                ? $"You are now wearing <@&{colourId}>."
                : "Your display colour has been removed.";
        }
        catch (ArgumentException ex) when (ex.ParamName == "colourId")
        {
            response = "That colour is no longer available to you.";
        }
        catch (KeyNotFoundException)
        {
            response = "The member is no longer in this guild.";
        }
        catch (InvalidDataException)
        {
            response = RoleColourReconciler.RolesChangedMessage;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Role colour update incomplete for guild {GuildId}, member {MemberId}",
                Context.Guild.Id, Context.User.Id);

            response = "Discord could not complete the colour update. Your roles may have changed; please try again.";
        }

        await FollowupAsync(response, ephemeral: true);

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