#pragma warning disable MA0048
using RatBot.Commands;
using RatBot.Infrastructure.Features.Meta;

namespace RatBot.Features.Meta.Commands;

[Group("meta", "Meta proposal commands.")]
public sealed class MetaModule(MetaProposalService metaProposalService, MetaProposalDiscordWorkflow workflow)
    : SlashCommandBase
{
    private static bool CanSubmitProposalModal(MetaProposalState state, MetaSuggestionSettings settings,
        IGuildUser user, uint hours)
    {
        return MetaCommandPermissions.IsAuthorOrAdmin(state, user)
               || MetaCommandPermissions.IsCabinet(settings, user)
               || (hours != MetaCommandIds.DefaultPollHours && MetaCommandPermissions.IsChairOrAdmin(settings, user));
    }

    [ModalInteraction($"{MetaCommandIds.ProposalModalPrefix}:*:*:*", true)]
    public async Task ProposalModalAsync(ulong userId, ulong threadId, uint hours, MetaProposalModal modal)
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

        ErrorOr<MetaProposalState> stateResult =
            await metaProposalService.GetForSuggestionThreadAsync(Context.Guild.Id, thread.Id);

        if (stateResult.IsError)
        {
            await FollowupAsync(stateResult.FirstError.Description, ephemeral: true);
            return;
        }

        if (Context.User is not IGuildUser user ||
            !CanSubmitProposalModal(stateResult.Value, settingsResult.Value, user, hours))
        {
            await FollowupAsync("You are not allowed to propose here.", ephemeral: true);
            return;
        }

        if (hours == 0)
        {
            await FollowupAsync("Poll duration must be at least 1 hour.", ephemeral: true);
            return;
        }

        if (hours != MetaCommandIds.DefaultPollHours &&
            !MetaCommandPermissions.IsChairOrAdmin(settingsResult.Value, user))
        {
            await FollowupAsync("Only administrators or the Cabinet Chair may override poll duration.",
                ephemeral: true);

            return;
        }

        if (stateResult.Value.Status is MetaProposalStatus.PollActive)
        {
            await FollowupAsync("A proposal poll is already active in this suggestion thread.", ephemeral: true);
            return;
        }

        await workflow.SendProposalContentAsync(thread, Context.User.Id, modal.ProposalTitle, modal.Summary,
            modal.Motivation, modal.Specification);

        ErrorOr<IUserMessage> pollResult = await workflow.CreateProposalPollAsync(thread, hours);

        if (pollResult.IsError)
        {
            await FollowupAsync(pollResult.FirstError.Description, ephemeral: true);
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        ErrorOr<MetaProposalState> startResult = await metaProposalService.StartPollAsync(
            Context.Guild.Id,
            thread.Id,
            Context.User.Id,
            modal.ProposalTitle,
            modal.Summary,
            modal.Motivation,
            modal.Specification,
            pollResult.Value.Id,
            now.AddHours(hours),
            now
        );

        await startResult.SwitchFirstAsync(
            async _ => await FollowupAsync("Proposal poll created.", ephemeral: true),
            async error => await FollowupAsync(error.Description, ephemeral: true)
        );
    }

    [SlashCommand("propose", "Create a proposal poll in this suggestion thread.")]
    public async Task ProposeAsync()
    {
        if (Context.Guild is null || Context.Channel is not IThreadChannel thread)
        {
            await RespondAsync("This command can only be used in a tracked suggestion thread.", ephemeral: true);
            return;
        }

        ErrorOr<MetaProposalState> stateResult =
            await metaProposalService.GetForSuggestionThreadAsync(Context.Guild.Id, thread.Id);

        if (stateResult.IsError)
        {
            await RespondAsync(stateResult.FirstError.Description, ephemeral: true);
            return;
        }

        if (Context.User is not IGuildUser user || !MetaCommandPermissions.IsAuthorOrAdmin(stateResult.Value, user))
        {
            await RespondAsync("Only the thread author may propose here.", ephemeral: true);
            return;
        }

        string customId =
            $"{MetaCommandIds.ProposalModalPrefix}:{Context.User.Id}:{thread.Id}:{MetaCommandIds.DefaultPollHours}";

        await Context.Interaction.RespondWithModalAsync<MetaProposalModal>(customId);
    }

    [ComponentInteraction($"{MetaCommandIds.ResubmitPrefix}:*", true)]
    public async Task ResubmitAsync(string id)
    {
        if (!Guid.TryParseExact(id, "N", out Guid stateId))
        {
            await RespondAsync("Invalid proposal state id.", ephemeral: true);
            return;
        }

        await DeferAsync(true);

        ErrorOr<MetaProposalState> retryStart =
            await metaProposalService.MarkPublicationRetryStartedAsync(stateId, DateTimeOffset.UtcNow);

        if (retryStart.IsError)
        {
            await FollowupAsync(retryStart.FirstError.Description, ephemeral: true);
            return;
        }

        MetaProposalState state = retryStart.Value;
        ErrorOr<MetaSuggestionSettings> settingsResult = await metaProposalService.GetSettingsAsync(state.GuildId);

        if (settingsResult.IsError)
        {
            await FollowupAsync(settingsResult.FirstError.Description, ephemeral: true);
            return;
        }

        ErrorOr<ulong> publishResult = await workflow.PublishProposalAsync(
            state,
            settingsResult.Value,
            state.PublicationRetryFailures >= MetaProposalState.MaxPublicationRetryFailuresBeforePing
        );

        if (!publishResult.IsError)
        {
            await metaProposalService.RecordPublishedAsync(state.Id, publishResult.Value);
            await FollowupAsync("Proposal publication retried successfully.", ephemeral: true);
            return;
        }

        ErrorOr<ulong> errorMessage =
            await workflow.PostPublicationErrorAsync(state, settingsResult.Value, state.PublicationErrorMessageId);

        if (!errorMessage.IsError)
            await metaProposalService.RecordPublicationFailureAsync(state.Id, errorMessage.Value,
                DateTimeOffset.UtcNow);

        await FollowupAsync(publishResult.FirstError.Description, ephemeral: true);
    }
}

public record MetaProposalModal : IModal
{
    [InputLabel("Motivation")]
    [ModalTextInput(
        "motivation",
        TextInputStyle.Paragraph,
        maxLength: 1950,
        placeholder: "Please provide a detailed explanation of what your proposal seeks to address. (1950 characters)"
    )]
    public required string Motivation { get; [UsedImplicitly] init; }

    [InputLabel("Title")]
    [ModalTextInput("title", maxLength: MetaProposalState.MaxTitleLength,
        placeholder: "The title of the proposal thread")]
    public required string ProposalTitle { get; [UsedImplicitly] init; }

    [InputLabel("Specification")]
    [ModalTextInput(
        "specification",
        TextInputStyle.Paragraph,
        maxLength: 1950,
        placeholder: "Please provide a concrete description of the proposed change or policy. (1950 characters)"
    )]
    public required string Specification { get; [UsedImplicitly] init; }

    [InputLabel("Summary")]
    [ModalTextInput(
        "summary",
        TextInputStyle.Paragraph,
        maxLength: 1500,
        placeholder: "Please provide a brief, high-level overview of your proposal. (1500 characters)"
    )]
    public required string Summary { get; [UsedImplicitly] init; }

    string IModal.Title => "Make a proposal";
}