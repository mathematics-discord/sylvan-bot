using Discord;
using Discord.WebSocket;
using RatBot.Domain.Common;
using RatBot.Domain.Features.Meta;
using RatBot.Features.Meta;
using Serilog.Core;
using Shouldly;

namespace RatBot.Discord.Tests.Features.Meta;

public sealed class MetaProposalDiscordWorkflowTests
{
    [Test]
    public async Task GetPollMessageAsync_NoPollRecorded_ReturnsNone()
    {
        // Arrange
        await using DiscordSocketClient client = new DiscordSocketClient();
        MetaProposalDiscordWorkflow workflow = new MetaProposalDiscordWorkflow(client, Logger.None);

        MetaProposalState state = MetaProposalState.TrackSuggestionThread(
            Guid.NewGuid(),
            guildId: 1,
            suggestionThreadChannelId: 2,
            suggestionsForumChannelId: 3,
            originalThreadAuthorUserId: 4,
            DateTimeOffset.UtcNow).Value;

        // Act
        Option<IUserMessage> result = await workflow.GetPollMessageAsync(state);

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public async Task GetPollMessageAsync_ThreadUnavailable_ReturnsNone()
    {
        // Arrange
        await using DiscordSocketClient client = new DiscordSocketClient();
        MetaProposalDiscordWorkflow workflow = new MetaProposalDiscordWorkflow(client, Logger.None);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        MetaProposalState state = MetaProposalState.TrackSuggestionThread(
            Guid.NewGuid(),
            guildId: 1,
            suggestionThreadChannelId: 2,
            suggestionsForumChannelId: 3,
            originalThreadAuthorUserId: 4,
            now).Value;

        state.StartPoll(
                proposalAuthorUserId: 4,
                title: "Proposal",
                summary: "Summary",
                motivation: "Motivation",
                specification: "Specification",
                pollMessageId: 5,
                pollExpiresAtUtc: now.AddHours(1),
                proposedAtUtc: now)
            .IsError.ShouldBeFalse();

        // Act
        Option<IUserMessage> result = await workflow.GetPollMessageAsync(state);

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public async Task GetPollMessageAsync_CancelledRequest_PropagatesCancellation()
    {
        // Arrange
        await using DiscordSocketClient client = new DiscordSocketClient();
        MetaProposalDiscordWorkflow workflow = new MetaProposalDiscordWorkflow(client, Logger.None);

        MetaProposalState state = MetaProposalState.TrackSuggestionThread(
            Guid.NewGuid(),
            guildId: 1,
            suggestionThreadChannelId: 2,
            suggestionsForumChannelId: 3,
            originalThreadAuthorUserId: 4,
            DateTimeOffset.UtcNow).Value;

        using CancellationTokenSource cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        OperationCanceledException exception =
            await Should.ThrowAsync<OperationCanceledException>(() =>
                workflow.GetPollMessageAsync(state, cancellation.Token));

        // Assert
        exception.CancellationToken.ShouldBe(cancellation.Token);
    }
}