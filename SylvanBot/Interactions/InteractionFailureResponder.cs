using Discord.Net;

namespace SylvanBot.Interactions;

internal static class InteractionFailureResponder
{
    private const string GenericFailureMessage = "An error occurred while processing that command.";

    public static async Task RespondToFailureAsync(IDiscordInteraction interaction, string message)
    {
        if (!interaction.HasResponded)
        {
            await interaction.RespondAsync(text: message, ephemeral: true, allowedMentions: AllowedMentions.None);

            return;
        }

        IUserMessage original;

        try
        {
            original = await interaction.GetOriginalResponseAsync();
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            await interaction.FollowupAsync(text: GenericFailureMessage, ephemeral: true, allowedMentions: AllowedMentions.None);

            return;
        }

        var sourceId = interaction switch
        {
            IComponentInteraction component => component.Message?.Id,
            IModalInteraction modal => modal.Message?.Id,
            _ => null,
        };
        var requiresOwnershipCheck = interaction is IComponentInteraction or IModalInteraction;
        var owned = !requiresOwnershipCheck || (sourceId.HasValue && sourceId.Value != original.Id);
        var flags = original.Flags ?? MessageFlags.None;
        var ephemeral = flags.HasFlag(MessageFlags.Ephemeral);
        var loading = flags.HasFlag(MessageFlags.Loading);

        var plainText =
            !flags.HasFlag(MessageFlags.ComponentsV2)
            && original.Components.Count == 0
            && original.Embeds.Count == 0
            && original.Attachments.Count == 0;
        var canEdit = owned && (loading || (ephemeral && plainText));

        if (canEdit)
        {
            var content = ephemeral ? message : GenericFailureMessage;

            await interaction.ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = content;
                properties.AllowedMentions = AllowedMentions.None;
            });

            return;
        }

        await interaction.FollowupAsync(text: message, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    public static string DescribeFailure(IResult result, ILogger interactionLogger)
    {
        var exception = result is ExecuteResult execution ? execution.Exception : null;

        // The SDK's generated module callback wraps command faults once before reporting completion.
        if (exception is InteractionException { InnerException: not null } sdkException)
            exception = sdkException.InnerException;

        string message;

        switch (exception)
        {
            case BotCommandException refusal:
                message = refusal.Message;

                if (refusal.InnerException is not null)
                    interactionLogger.Warning(exception: refusal, "Command operation failed");
                else
                    interactionLogger.Information("Command refused: {Reason}", propertyValue: message);

                break;
            case OperationCanceledException:
                message = "Command processing was cancelled.";
                interactionLogger.Information("Command processing was cancelled");

                break;
            case not null:
                message = GenericFailureMessage;
                interactionLogger.Error(exception: exception, "Unexpected command failure");

                break;
            default:
                message = result.Error switch
                {
                    InteractionCommandError.UnmetPrecondition => string.IsNullOrWhiteSpace(result.ErrorReason)
                        ? "Command precondition failed."
                        : $"Command failed: {result.ErrorReason}",
                    InteractionCommandError.UnknownCommand => "That command is unavailable.",
                    InteractionCommandError.BadArgs or InteractionCommandError.ParseFailed or InteractionCommandError.ConvertFailed =>
                        "The command arguments could not be understood.",
                    _ => GenericFailureMessage,
                };
                interactionLogger.Information("Command failed: {CommandError}", propertyValue: result.Error);

                break;
        }

        return message;
    }
}