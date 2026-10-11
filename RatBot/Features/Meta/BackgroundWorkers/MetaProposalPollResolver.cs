using RatBot.Infrastructure.Features.Meta;

namespace RatBot.Features.Meta.BackgroundWorkers;

public sealed class MetaProposalPollResolver(MetaProposalDiscordWorkflow workflow, ILogger logger)
{
    private const int MaxFinalizationRetries = 3;
    private readonly ILogger _logger = logger.ForContext<MetaProposalPollResolver>();

    private static bool SubmitWon(Poll poll, PollResults results)
    {
        PollAnswer submitAnswer = poll.Answers.FirstOrDefault(answer =>
            string.Equals(answer.PollMedia.Text, "Submit", StringComparison.Ordinal));

        PollAnswer doNotSubmitAnswer = poll.Answers.FirstOrDefault(answer =>
            string.Equals(answer.PollMedia.Text, "Do Not Submit", StringComparison.Ordinal)
        );

        if (submitAnswer.AnswerId == 0 || doNotSubmitAnswer.AnswerId == 0)
            return false;

        uint submitCount = results.AnswerCounts.FirstOrDefault(count => count.AnswerId == submitAnswer.AnswerId).Count;

        uint doNotSubmitCount = results.AnswerCounts
            .FirstOrDefault(count => count.AnswerId == doNotSubmitAnswer.AnswerId).Count;

        return submitCount > 0 && submitCount > doNotSubmitCount;
    }

    public async Task ResolveExpiredPollAsync(MetaProposalService service, MetaProposalState state,
        CancellationToken ct)
    {
        IUserMessage? pollMessage = await workflow.GetPollMessageAsync(state, ct);

        if (pollMessage is null)
        {
            await service.ClearDeletedPollAsync(state.Id, ct);
            return;
        }

        await ResolvePollMessageAsync(service, state, pollMessage, true, ct);
    }

    public Task ResolveFinalizedPollAsync(MetaProposalService service, MetaProposalState state,
        IUserMessage pollMessage, CancellationToken ct)
    {
        return ResolvePollMessageAsync(service, state, pollMessage, false, ct);
    }

    private async Task CompletePollAsync(MetaProposalService service, MetaProposalState state, bool submitWon,
        CancellationToken ct)
    {
        ErrorOr<MetaProposalState> completed = await service.CompletePollAsync(state.Id, submitWon, ct);

        if (completed.IsError)
            return;

        MetaProposalState updated = completed.Value;

        if (!submitWon)
        {
            if (updated.Status is MetaProposalStatus.Closed)
                await workflow.LockArchiveThreadAsync(updated.SuggestionThreadChannelId);

            return;
        }

        ErrorOr<MetaSuggestionSettings> settingsResult = await service.GetSettingsAsync(updated.GuildId, ct);

        if (settingsResult.IsError)
            return;

        ErrorOr<ulong> publishResult = await workflow.PublishProposalAsync(updated, settingsResult.Value, false, ct);

        if (!publishResult.IsError)
        {
            await service.RecordPublishedAsync(updated.Id, publishResult.Value, ct);
            await workflow.LockArchiveThreadAsync(updated.SuggestionThreadChannelId);
            return;
        }

        ErrorOr<ulong> errorMessage =
            await workflow.PostPublicationErrorAsync(updated, settingsResult.Value, updated.PublicationErrorMessageId,
                ct);

        if (!errorMessage.IsError)
            await service.RecordPublicationFailureAsync(updated.Id, errorMessage.Value, DateTimeOffset.UtcNow, ct);
    }

    private async Task ResolvePollMessageAsync(
        MetaProposalService service,
        MetaProposalState state,
        IUserMessage pollMessage,
        bool requestFinalization,
        CancellationToken ct
    )
    {
        Poll? pollValue = pollMessage.Poll;

        if (pollValue is null)
        {
            if (requestFinalization)
                await CompletePollAsync(service, state, false, ct);

            return;
        }

        Poll poll = pollValue.Value;

        if (poll.Results is not { IsFinalized: true } results)
        {
            if (!requestFinalization)
                return;

            if (state.PollFinalizationRetries >= MaxFinalizationRetries)
            {
                await CompletePollAsync(service, state, false, ct);
                return;
            }

            try
            {
                await pollMessage.EndPollAsync(null);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not request finalization for meta proposal poll {PollMessageId}",
                    pollMessage.Id);
            }

            await service.RecordPollFinalizationRetryAsync(state.Id, ct);
            return;
        }

        await CompletePollAsync(service, state, SubmitWon(poll, results), ct);
    }
}