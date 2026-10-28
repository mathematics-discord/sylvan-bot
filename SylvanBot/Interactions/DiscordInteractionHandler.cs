using System.Collections.Concurrent;
using System.Diagnostics;
using SylvanBot.Gateway;

namespace SylvanBot.Interactions;

public sealed class DiscordInteractionHandler(
    DiscordSocketClient discordClient,
    InteractionService interactionService,
    IServiceProvider services,
    InteractionDiagnostics diagnostics,
    InteractionCommandRegistrar registrar,
    ILogger logger
) : IDiscordGatewayHandler
{
    private readonly ConcurrentDictionary<ulong, Stopwatch> _interactionStopwatches = [];
    private readonly ILogger _logger = logger.ForContext<DiscordInteractionHandler>();

    public async Task InitializeAsync(CancellationToken ct)
    {
        await registrar.LoadModulesAsync();

        discordClient.InteractionCreated += HandleInteractionAsync;
        discordClient.Ready += registrar.RegisterCommandsAsync;
        discordClient.JoinedGuild += registrar.RegisterCommandsToGuildAsync;
        interactionService.InteractionExecuted += HandleInteractionExecutedAsync;
    }

    public void Unsubscribe()
    {
        discordClient.InteractionCreated -= HandleInteractionAsync;
        discordClient.Ready -= registrar.RegisterCommandsAsync;
        discordClient.JoinedGuild -= registrar.RegisterCommandsToGuildAsync;
        interactionService.InteractionExecuted -= HandleInteractionExecutedAsync;
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        if (interaction.Type == InteractionType.ApplicationCommandAutocomplete)
        {
            // Autocomplete has its own response shape; do not send ordinary command failure text.
            var autocompleteContext = new SocketInteractionContext(client: discordClient, interaction: interaction);
            var autocompleteResult = await interactionService.ExecuteCommandAsync(context: autocompleteContext, services: services);

            if (!autocompleteResult.IsSuccess)
                _logger.Warning("Autocomplete failed: {Reason}", propertyValue: autocompleteResult.ErrorReason);

            return;
        }

        if (interaction.Type is not (InteractionType.ApplicationCommand or InteractionType.ModalSubmit or InteractionType.MessageComponent))
            return;

        IInteractionContext context = new SocketInteractionContext(client: discordClient, interaction: interaction);
        var usage = InteractionDiagnostics.GetCommandUsageDetails(interaction);

        using var scope = InteractionLogScope.Begin(
            diagnostics.CreateInteractionLogScopeDetails(interaction: interaction, context: context, usage: usage));
        InteractionDiagnostics.LogCommandUsage(diagnostics.CreateInteractionDiagnosticsLogger(context), context: context, usage: usage);
        await HandleInteractionAsync(context);
    }

    private async Task HandleInteractionAsync(IInteractionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        _interactionStopwatches[context.Interaction.Id] = stopwatch;

        try
        {
            var result = await interactionService.ExecuteCommandAsync(context: context, services: services);

            diagnostics
                .CreateInteractionDiagnosticsLogger(context)
                .ForContext("event_kind", "interaction.dispatch_completed")
                .Debug("Interaction dispatched in {DispatchMs} ms", propertyValue: stopwatch.Elapsed.TotalMilliseconds);

            if (!result.IsSuccess)
                await CompleteInteractionAsync(command: null, context: context, result: result);
        }
        catch (Exception ex)
        {
            await CompleteInteractionAsync(command: null, context: context, ExecuteResult.FromError(ex));
        }
    }

    private Task HandleInteractionExecutedAsync(ICommandInfo? command, IInteractionContext context, IResult result) =>
        CompleteInteractionAsync(command: command, context: context, result: result);

    private async Task CompleteInteractionAsync(ICommandInfo? command, IInteractionContext context, IResult result)
    {
        if (!_interactionStopwatches.TryRemove(key: context.Interaction.Id, out var stopwatch))
            return;

        var interactionLogger = _logger;

        try
        {
            interactionLogger = diagnostics
                .CreateInteractionDiagnosticsLogger(context)
                .ForContext("event_kind", "interaction.execution_completed")
                .ForContext("command_module", value: command?.Module.Name)
                .ForContext("command_group", value: command?.Module.SlashGroupName)
                .ForContext("command_name", value: command?.Name)
                .ForContext("user_id", value: context.User.Id)
                .ForContext("guild_id", value: context.Guild?.Id)
                .ForContext("channel_id", value: context.Channel?.Id)
                .ForContext("total_ms", Math.Round(value: stopwatch.Elapsed.TotalMilliseconds, digits: 2));

            if (result.IsSuccess)
            {
                interactionLogger.Debug("Interaction completed in {TotalMs} ms", propertyValue: stopwatch.Elapsed.TotalMilliseconds);

                return;
            }

            var message = InteractionFailureResponder.DescribeFailure(result, interactionLogger);

            await InteractionFailureResponder.RespondToFailureAsync(interaction: context.Interaction, message: message);
        }
        catch (Exception ex)
        {
            interactionLogger
                .ForContext("event_kind", "interaction.response_failed")
                .Warning(exception: ex, "Failed to complete interaction error handling");
        }
    }
}