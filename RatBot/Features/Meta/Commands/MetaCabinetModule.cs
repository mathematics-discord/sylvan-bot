#pragma warning disable MA0048
using RatBot.Commands;
using RatBot.Infrastructure.Features.Meta;

namespace RatBot.Features.Meta.Commands;

[Group("meta-cabinet", "Cabinet meta commands.")]
[DefaultMemberPermissions(GuildPermission.BanMembers)]
public sealed class MetaCabinetModule(MetaProposalService metaProposalService, MetaProposalDiscordWorkflow workflow)
    : SlashCommandBase
{
    [SlashCommand("close", "End the suggestion poll immediately.")]
    [RequireUserPermission(GuildPermission.BanMembers)]
    public async Task CloseAsync()
    {
        if (Context.Guild is null || Context.Channel is not IThreadChannel thread)
        {
            await RespondAsync("This command can only be used in a tracked suggestion thread.", ephemeral: true);
            return;
        }

        ErrorOr<MetaSuggestionSettings> settingsResult = await metaProposalService.GetSettingsAsync(Context.Guild.Id);

        if (settingsResult.IsError)
        {
            await RespondAsync(settingsResult.FirstError.Description, ephemeral: true);
            return;
        }

        if (Context.User is not IGuildUser user || !MetaCommandPermissions.IsChairOrAdmin(settingsResult.Value, user))
        {
            await RespondAsync("Only the Cabinet Chair or administrators may use this command.", ephemeral: true);
            return;
        }

        ErrorOr<MetaProposalState> stateResult =
            await metaProposalService.GetForSuggestionThreadAsync(Context.Guild.Id, thread.Id);

        if (stateResult.IsError)
        {
            await RespondAsync(stateResult.FirstError.Description, ephemeral: true);
            return;
        }

        MetaProposalState state = stateResult.Value;

        if (state.Status is not MetaProposalStatus.PollActive)
        {
            await RespondAsync("There is no active poll for this suggestion.", ephemeral: true);
            return;
        }

        IUserMessage? pollMessage = await workflow.GetPollMessageAsync(state);

        if (pollMessage?.Poll is null)
        {
            await RespondAsync("The poll message could not be found or does not contain a poll.", ephemeral: true);
            return;
        }

        await DeferAsync(true);

        try
        {
            await pollMessage.EndPollAsync(null);
            await FollowupAsync("Poll ended immediately.", ephemeral: true);
        }
        catch (Exception)
        {
            await FollowupAsync("Failed to end the poll.", ephemeral: true);
        }
    }

    [SlashCommand("propose", "Create a Cabinet proposal poll.")]
    [RequireUserPermission(GuildPermission.BanMembers)]
    public async Task ProposeAsync(
        [Summary("hours", "Poll duration in hours.")] uint hours = MetaCommandIds.DefaultPollHours)
    {
        if (Context.Guild is null || Context.Channel is not IThreadChannel thread)
        {
            await RespondAsync("This command can only be used in a tracked suggestion thread.", ephemeral: true);
            return;
        }

        ErrorOr<MetaSuggestionSettings> settingsResult = await metaProposalService.GetSettingsAsync(Context.Guild.Id);

        if (settingsResult.IsError)
        {
            await RespondAsync(settingsResult.FirstError.Description, ephemeral: true);
            return;
        }

        if (Context.User is not IGuildUser user || !MetaCommandPermissions.IsCabinet(settingsResult.Value, user))
        {
            await RespondAsync("Only Cabinet, Cabinet Chair, or administrators may use this command.", ephemeral: true);
            return;
        }

        if (hours != MetaCommandIds.DefaultPollHours &&
            !MetaCommandPermissions.IsChairOrAdmin(settingsResult.Value, user))
        {
            await RespondAsync("Only administrators or the Cabinet Chair may override poll duration.", ephemeral: true);
            return;
        }

        if (hours == 0)
        {
            await RespondAsync("Poll duration must be at least 1 hour.", ephemeral: true);
            return;
        }

        ErrorOr<MetaProposalState> stateResult =
            await metaProposalService.GetForSuggestionThreadAsync(Context.Guild.Id, thread.Id);

        if (stateResult.IsError)
        {
            await RespondAsync(stateResult.FirstError.Description, ephemeral: true);
            return;
        }

        string customId = $"{MetaCommandIds.ProposalModalPrefix}:{Context.User.Id}:{thread.Id}:{hours}";
        await Context.Interaction.RespondWithModalAsync<MetaProposalModal>(customId);
    }

    [SlashCommand("veto", "Veto this meta thread.")]
    [RequireUserPermission(GuildPermission.BanMembers)]
    public async Task VetoAsync()
    {
        if (Context.Guild is null || Context.Channel is not IThreadChannel thread)
        {
            await RespondAsync("This command can only be used in a tracked meta thread.", ephemeral: true);
            return;
        }

        ErrorOr<MetaSuggestionSettings> settingsResult = await metaProposalService.GetSettingsAsync(Context.Guild.Id);

        if (settingsResult.IsError)
        {
            await RespondAsync(settingsResult.FirstError.Description, ephemeral: true);
            return;
        }

        if (Context.User is not IGuildUser user || !MetaCommandPermissions.IsOwnerOrChair(settingsResult.Value, user))
        {
            await RespondAsync("Only the guild owner, Cabinet Chair, or administrators may veto.", ephemeral: true);
            return;
        }

        ErrorOr<MetaProposalState> stateResult =
            await metaProposalService.GetForAnyThreadAsync(Context.Guild.Id, thread.Id);

        if (stateResult.IsError)
        {
            await RespondAsync(stateResult.FirstError.Description, ephemeral: true);
            return;
        }

        string customId = $"{MetaCommandIds.VetoModalPrefix}:{Context.User.Id}:{thread.Id}";
        await Context.Interaction.RespondWithModalAsync<MetaVetoModal>(customId);
    }

    [ModalInteraction($"{MetaCommandIds.VetoModalPrefix}:*:*", true)]
    public async Task VetoModalAsync(ulong userId, ulong threadId, MetaVetoModal modal)
    {
        if (Context.User.Id != userId)
        {
            await RespondAsync("Only the user who opened this modal can submit it.", ephemeral: true);
            return;
        }

        if (Context.Guild is null || Context.Channel is not IThreadChannel thread || thread.Id != threadId)
        {
            await RespondAsync("This modal can only be submitted in the thread where it was opened.", ephemeral: true);
            return;
        }

        await DeferAsync(true);

        ErrorOr<MetaSuggestionSettings> settingsResult = await metaProposalService.GetSettingsAsync(Context.Guild.Id);

        if (settingsResult.IsError)
        {
            await FollowupAsync(settingsResult.FirstError.Description, ephemeral: true);
            return;
        }

        if (Context.User is not IGuildUser user || !MetaCommandPermissions.IsOwnerOrChair(settingsResult.Value, user))
        {
            await FollowupAsync("Only the guild owner, Cabinet Chair, or administrators may veto.", ephemeral: true);
            return;
        }

        ErrorOr<MetaProposalState> vetoResult = await metaProposalService.VetoAsync(
            Context.Guild.Id,
            thread.Id,
            user.Id,
            modal.Reason,
            DateTimeOffset.UtcNow
        );

        if (vetoResult.IsError)
        {
            await FollowupAsync(vetoResult.FirstError.Description, ephemeral: true);
            return;
        }

        await workflow.PostVetoAsync(vetoResult.Value);
        await FollowupAsync("Veto recorded.", ephemeral: true);
    }
}

public record MetaVetoModal : IModal
{
    [InputLabel("Reason")]
    [ModalTextInput("reason", TextInputStyle.Paragraph, maxLength: 1950, placeholder: "Reason for veto")]
    public required string Reason { get; [UsedImplicitly] init; }

    string IModal.Title => "Veto proposal";
}