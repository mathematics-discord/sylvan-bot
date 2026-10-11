using RatBot.Features.Meta.Commands;

namespace RatBot.Features.Meta;

public sealed class MetaProposalDiscordWorkflow(DiscordSocketClient client, ILogger logger)
{
    private static readonly AllowedMentions NoMentions = AllowedMentions.None;
    private static readonly AllowedMentions RoleMentionsOnly = new AllowedMentions(AllowedMentionTypes.Roles);
    private readonly ILogger _logger = logger.ForContext<MetaProposalDiscordWorkflow>();

    public static string BuildProposalText(MetaProposalState state)
    {
        if (!state.HasProposalText)
            return "Proposal text is missing.";

        return string.Join("\n\n", BuildFirstPost(state), BuildSecondPost(state), BuildThirdPost(state));
    }

    private static string BuildFirstPost(MetaProposalState state) =>
        $"""
            ## Author
            <@{state.ProposalAuthorUserId!.Value}>

            ## Date
            <t:{state.ProposedAtUtc!.Value.ToUnixTimeSeconds()}:F>

            ## Summary
            {state.Summary}
            """;

    private static string BuildSecondPost(MetaProposalState state) =>
        $"""
            ## Motivation
            {state.Motivation}
            """;

    private static string BuildThirdPost(MetaProposalState state) =>
        $"""
            ## Specification
            {state.Specification}
            """;

    private static async Task SendCabinetPublicationFailureNoticeAsync(
        MetaProposalState state,
        MetaSuggestionSettings settings,
        IThreadChannel thread
    ) =>
        await thread.SendMessageAsync(
            $"<@&{settings.CabinetRoleId}> recovered proposal publication after {state.PublicationRetryFailures} failed retry attempts.",
            allowedMentions: RoleMentionsOnly
        );

    public async Task<ErrorOr<IUserMessage>> CreateProposalPollAsync(
        IThreadChannel suggestionThread,
        uint durationHours,
        CancellationToken ct = default
    )
    {
        _ = ct;

        PollProperties poll = new PollProperties
        {
            Question = new PollMediaProperties { Text = "Submit this proposal?" },
            Answers =
            [
                new PollMediaProperties { Text = "Submit", Emoji = new global::Discord.Emoji("✅") },
                new PollMediaProperties { Text = "Do Not Submit", Emoji = new global::Discord.Emoji("❌") },
            ],
            Duration = durationHours,
            AllowMultiselect = false,
            LayoutType = PollLayout.Default,
        };

        IUserMessage message = await suggestionThread.SendMessageAsync("Proposal poll", allowedMentions: NoMentions, poll: poll);

        return ErrorOrFactory.From(message);
    }

    public async Task<IUserMessage?> GetPollMessageAsync(MetaProposalState state, CancellationToken ct = default)
    {
        _ = ct;

        if (state.PollMessageId is null)
            return null;

        IChannel? channel = client.GetChannel(state.SuggestionThreadChannelId);

        if (channel is not IThreadChannel thread)
            return null;

        IMessage? message = await thread.GetMessageAsync(state.PollMessageId.Value);
        return message as IUserMessage;
    }

    public async Task LockArchiveThreadAsync(ulong threadChannelId)
    {
        if (client.GetChannel(threadChannelId) is not IThreadChannel thread)
            return;

        await thread.ModifyAsync(properties =>
        {
            properties.Locked = true;
            properties.Archived = true;
        });
    }

    public async Task<ErrorOr<ulong>> PostPublicationErrorAsync(
        MetaProposalState state,
        MetaSuggestionSettings settings,
        ulong? previousErrorMessageId = null,
        CancellationToken ct = default
    )
    {
        _ = ct;

        IChannel? channel = client.GetChannel(state.SuggestionThreadChannelId);

        if (channel is not IThreadChannel thread)
            return Error.NotFound("MetaProposal.SuggestionThreadNotFound", "The suggestion thread was not found.");

        bool pingCabinet = state.PublicationRetryFailures + 1 >= MetaProposalState.MaxPublicationRetryFailuresBeforePing;

        if (pingCabinet && previousErrorMessageId is { } previousMessageId)
            try
            {
                await thread.DeleteMessageAsync(previousMessageId);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not delete previous meta publication error message {MessageId}", previousMessageId);
            }

        string content = pingCabinet ? $"<@&{settings.CabinetRoleId}> Proposal publication failed repeatedly." : "Proposal publication failed.";

        AllowedMentions mentions = pingCabinet ? RoleMentionsOnly : NoMentions;

        MessageComponent components = new ComponentBuilder().WithButton("Attempt Resubmit", MetaCommandIds.ResubmitCustomId(state.Id)).Build();

        IUserMessage message = await thread.SendMessageAsync(content, allowedMentions: mentions, components: components);

        return message.Id;
    }

    public async Task PostVetoAsync(MetaProposalState state)
    {
        IChannel? channel = client.GetChannel(state.ProposalThreadChannelId ?? state.SuggestionThreadChannelId);

        if (channel is not IThreadChannel thread)
            return;

        await thread.SendMessageAsync(
            $"""
            Proposal vetoed.

            {state.VetoReason}
            """,
            allowedMentions: NoMentions
        );

        await LockArchiveThreadAsync(thread.Id);
    }

    public async Task<ErrorOr<ulong>> PublishProposalAsync(
        MetaProposalState state,
        MetaSuggestionSettings settings,
        bool pingCabinet,
        CancellationToken ct = default
    )
    {
        _ = ct;

        if (!state.HasProposalText)
            return Error.Validation("MetaProposal.TextMissing", "Proposal text is missing.");

        IChannel? channel = client.GetChannel(settings.ProposalsForumChannelId);

        if (channel is not IForumChannel forumChannel)
            return Error.NotFound("MetaProposal.ProposalsForumNotFound", "The configured proposals forum was not found.");

        IThreadChannel thread = await forumChannel.CreatePostAsync(state.ProposalTitle!, text: BuildFirstPost(state), allowedMentions: NoMentions);

        await thread.SendMessageAsync(BuildSecondPost(state), allowedMentions: NoMentions);
        await thread.SendMessageAsync(BuildThirdPost(state), allowedMentions: NoMentions);

        if (thread.IsArchived || thread.IsLocked)
            await thread.ModifyAsync(properties =>
            {
                properties.Archived = false;
                properties.Locked = false;
            });

        await thread.SendMessageAsync($"<@&{settings.CabinetRoleId}> <@&{settings.CommitteeRoleId}>", allowedMentions: RoleMentionsOnly);

        if (pingCabinet)
            await SendCabinetPublicationFailureNoticeAsync(state, settings, thread);

        return thread.Id;
    }

    public async Task SendProposalContentAsync(
        IThreadChannel suggestionThread,
        ulong authorId,
        string title,
        string summary,
        string motivation,
        string specification
    )
    {
        ComponentBuilderV2 builder = new ComponentBuilderV2(
            new ContainerBuilder()
                .WithAccentColor(Color.Teal)
                .WithTextDisplay(new TextDisplayBuilder().WithContent($"# {title}"))
                .WithTextDisplay(
                    new TextDisplayBuilder().WithContent($"**Author:** <@{authorId}>\n**Date:** <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>")
                ),
            new ContainerBuilder()
                .WithAccentColor(Color.Teal)
                .WithTextDisplay(new TextDisplayBuilder().WithContent("## Summary"))
                .WithTextDisplay(new TextDisplayBuilder().WithContent(summary)),
            new ContainerBuilder()
                .WithAccentColor(Color.Teal)
                .WithTextDisplay(new TextDisplayBuilder().WithContent("## Motivation"))
                .WithTextDisplay(new TextDisplayBuilder().WithContent(motivation)),
            new ContainerBuilder()
                .WithAccentColor(Color.Teal)
                .WithTextDisplay(new TextDisplayBuilder().WithContent("## Specification"))
                .WithTextDisplay(new TextDisplayBuilder().WithContent(specification))
        );

        await suggestionThread.SendMessageAsync(components: builder.Build(), flags: MessageFlags.ComponentsV2, allowedMentions: NoMentions);
    }

    public async Task UnlockThreadAsync(ulong threadChannelId)
    {
        if (client.GetChannel(threadChannelId) is not IThreadChannel thread)
            return;

        await thread.ModifyAsync(properties =>
        {
            properties.Archived = false;
            properties.Locked = false;
        });
    }
}
