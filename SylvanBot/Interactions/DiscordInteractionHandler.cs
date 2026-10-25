namespace SylvanBot.Interactions;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Discord.Net;
using SylvanBot.Application.Common;
using SylvanBot.Features.Hello;
using SylvanBot.Gateway;
using IResult = Discord.Interactions.IResult;

public sealed class DiscordInteractionHandler(
    DiscordSocketClient discordClient,
    InteractionService interactionService,
    IServiceProvider services,
    ILogger logger) : IDiscordGatewayHandler
{
    private const string DiagEventName = "interaction_diagnostics";
    private const string GenericFailureMessage = "An error occurred while processing that command.";
    private readonly ConcurrentDictionary<ulong, Stopwatch> _interactionStopwatches = [];
    private readonly ILogger _logger = logger.ForContext<DiscordInteractionHandler>();

    private readonly string _serviceInstanceId = Environment.MachineName;

    private static IEnumerable<ModuleInfo> EnumerateModules(IEnumerable<ModuleInfo> modules)
    {
        foreach (var module in modules)
        {
            yield return module;

            foreach (var subModule in EnumerateModules(module.SubModules))
                yield return subModule;
        }
    }

    private static IEnumerable<SocketSlashCommandDataOption> EnumerateSlashOptions(
        IReadOnlyCollection<SocketSlashCommandDataOption> options)
    {
        foreach (var option in options)
        {
            yield return option;

            foreach (var nested in EnumerateSlashOptions(option.Options))
                yield return nested;
        }
    }

    private static CommandUsageDetails GetCommandUsageDetails(SocketInteraction interaction)
    {
        return interaction switch
        {
            SocketSlashCommand slashCommand => GetSlashUsageDetails(slashCommand),
            SocketUserCommand userCommand => GetUserContextUsageDetails(userCommand),
            SocketMessageCommand messageCommand => GetMessageContextUsageDetails(messageCommand),
            _ => new CommandUsageDetails(GetInteractionName(interaction), null, null, null)
        };
    }

    private static string GetInteractionName(SocketInteraction interaction)
    {
        return interaction switch
        {
            SocketSlashCommand slashCommand => GetSlashCommandName(slashCommand),
            SocketUserCommand userCommand => userCommand.Data.Name,
            SocketMessageCommand messageCommand => messageCommand.Data.Name,
            SocketMessageComponent component => component.Data.CustomId,
            SocketModal modal => modal.Data.CustomId,
            _ => interaction.Type.ToString()
        };
    }

    private static CommandUsageDetails GetMessageContextUsageDetails(SocketMessageCommand messageCommand)
    {
        IUser? invokee = messageCommand.Data.Message.Author;

        return new CommandUsageDetails(messageCommand.Data.Name, invokee?.Id, invokee?.Username,
            "message_context_author");
    }

    private static string GetSlashCommandName(SocketSlashCommand command)
    {
        List<string> parts = [command.Data.Name];
        IReadOnlyCollection<SocketSlashCommandDataOption> options = command.Data.Options;

        while (true)
        {
            var subCommandOption = options.FirstOrDefault(option =>
                option.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup
            );

            if (subCommandOption is null)
                return string.Join(" ", parts);

            parts.Add(subCommandOption.Name);
            options = subCommandOption.Options;
        }
    }

    private static CommandUsageDetails GetSlashUsageDetails(SocketSlashCommand slashCommand)
    {
        var commandName = GetSlashCommandName(slashCommand);

        foreach (var option in EnumerateSlashOptions(slashCommand.Data.Options))
        {
            if (option.Type is not (ApplicationCommandOptionType.User or ApplicationCommandOptionType.Mentionable))
                continue;

            switch (option.Value)
            {
                case SocketGuildUser guildUser:
                    return new CommandUsageDetails(commandName, guildUser.Id, guildUser.Username,
                        $"slash_option:{option.Name}");
                case SocketUser socketUser:
                    return new CommandUsageDetails(commandName, socketUser.Id, socketUser.Username,
                        $"slash_option:{option.Name}");
                case IUser user:
                    return new CommandUsageDetails(commandName, user.Id, user.Username, $"slash_option:{option.Name}");
                case ulong userId:
                    return new CommandUsageDetails(commandName, userId, null, $"slash_option:{option.Name}");
            }
        }

        return new CommandUsageDetails(commandName, null, null, null);
    }

    private static CommandUsageDetails GetUserContextUsageDetails(SocketUserCommand userCommand)
    {
        var invokee = userCommand.Data.Member ?? ((IUserCommandInteractionData)userCommand.Data).User;
        return new CommandUsageDetails(userCommand.Data.Name, invokee?.Id, invokee?.Username, "user_context_target");
    }

    private static void LogCommandUsage(ILogger interactionLogger, IInteractionContext context,
        CommandUsageDetails usage)
    {
        interactionLogger
            .ForContext("method_context", $"{nameof(DiscordInteractionHandler)}.{nameof(LogCommandUsage)}")
            .ForContext("event_kind", "interaction.command_invoked")
            .ForContext("component", "interaction_dispatch")
            .ForContext("outcome", "received")
            .ForContext("command_name", usage.CommandName)
            .ForContext("invoker_user_id", context.User.Id)
            .ForContext("invokee_user_id", usage.InvokeeUserId)
            .ForContext("invokee_username", usage.InvokeeUsername)
            .ForContext("invokee_source", usage.InvokeeSource)
            .Debug("Command invoked");
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        var interactionsAssembly = typeof(HelloModule).Assembly;
        await interactionService.AddModulesAsync(interactionsAssembly, services);

        _logger.Information("Registered {InteractionModuleCount} interaction modules",
            interactionService.Modules.Count);

        LogRegisteredInteractionCommands();

        discordClient.InteractionCreated += HandleInteractionAsync;
        discordClient.Ready += RegisterCommandsAsync;
        discordClient.JoinedGuild += RegisterCommandsToGuildAsync;
        interactionService.InteractionExecuted += HandleInteractionExecutedAsync;
    }

    public void Unsubscribe()
    {
        discordClient.InteractionCreated -= HandleInteractionAsync;
        discordClient.Ready -= RegisterCommandsAsync;
        discordClient.JoinedGuild -= RegisterCommandsToGuildAsync;
        interactionService.InteractionExecuted -= HandleInteractionExecutedAsync;
    }

    private ILogger CreateInteractionDiagnosticsLogger(IInteractionContext context)
    {
        var interactionLogger = _logger
            .ForContext("diag_event", DiagEventName)
            .ForContext("service_instance_id", _serviceInstanceId)
            .ForContext("process_id", Environment.ProcessId)
            .ForContext("interaction_id", context.Interaction.Id)
            .ForContext("interaction_type", context.Interaction.Type.ToString());

        return context.Interaction is SocketInteraction socketInteraction
            ? interactionLogger
                .ForContext("interaction_name", GetInteractionName(socketInteraction))
                .ForContext("interaction_created_at_utc", socketInteraction.CreatedAt.UtcDateTime.ToString("O"))
            : interactionLogger;
    }

    private InteractionLogScopeDetails CreateInteractionLogScopeDetails(
        SocketInteraction interaction,
        IInteractionContext context,
        CommandUsageDetails usage
    )
    {
        return new InteractionLogScopeDetails(
            _serviceInstanceId,
            Environment.ProcessId,
            interaction.Id,
            interaction.Type.ToString(),
            GetInteractionName(interaction),
            interaction.CreatedAt.UtcDateTime.ToString("O"),
            context.User.Id,
            context.Guild?.Id,
            context.Channel?.Id,
            interaction.Type == InteractionType.ApplicationCommand ? usage.CommandName : null
        );
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        if (interaction.Type is not (InteractionType.ApplicationCommand or InteractionType.ModalSubmit
            or InteractionType.MessageComponent))
            return;

        IInteractionContext context = new SocketInteractionContext(discordClient, interaction);
        var usage = GetCommandUsageDetails(interaction);
        using var scope =
            InteractionLogScope.Begin(CreateInteractionLogScopeDetails(interaction, context, usage));

        LogCommandUsage(CreateInteractionDiagnosticsLogger(context), context, usage);
        await HandleInteractionAsync(context);
    }

    private async Task HandleInteractionAsync(IInteractionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        _interactionStopwatches[context.Interaction.Id] = stopwatch;

        try
        {
            var result = await interactionService.ExecuteCommandAsync(context, services);
            CreateInteractionDiagnosticsLogger(context)
                .ForContext("event_kind", "interaction.dispatch_completed")
                .Debug("Interaction dispatched in {DispatchMs} ms", stopwatch.Elapsed.TotalMilliseconds);

            if (!result.IsSuccess)
                await CompleteInteractionAsync(null, context, result);
        }
        catch (Exception ex)
        {
            await CompleteInteractionAsync(null, context, ExecuteResult.FromError(ex));
        }
    }

    private Task HandleInteractionExecutedAsync(ICommandInfo? command, IInteractionContext context, IResult result)
    {
        return CompleteInteractionAsync(command, context, result);
    }

    private async Task CompleteInteractionAsync(ICommandInfo? command, IInteractionContext context, IResult result)
    {
        if (!_interactionStopwatches.TryRemove(context.Interaction.Id, out var stopwatch))
            return;

        var interactionLogger = _logger;

        try
        {
            interactionLogger = CreateInteractionDiagnosticsLogger(context)
                .ForContext("event_kind", "interaction.execution_completed")
                .ForContext("command_module", command?.Module.Name)
                .ForContext("command_group", command?.Module.SlashGroupName)
                .ForContext("command_name", command?.Name)
                .ForContext("user_id", context.User.Id)
                .ForContext("guild_id", context.Guild?.Id)
                .ForContext("channel_id", context.Channel?.Id)
                .ForContext("total_ms", Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2));

            if (result.IsSuccess)
            {
                interactionLogger.Debug("Interaction completed in {TotalMs} ms", stopwatch.Elapsed.TotalMilliseconds);
                return;
            }

            var exception = result is ExecuteResult execution ? execution.Exception : null;

            // The SDK's generated module callback wraps command faults once before reporting completion.
            if (exception is Discord.Interactions.InteractionException { InnerException: not null } sdkException)
                exception = sdkException.InnerException;

            string message;

            switch (exception)
            {
                case BotCommandException refusal:
                    message = refusal.Message;
                    if (refusal.InnerException is not null)
                        interactionLogger.Warning(refusal, "Command operation failed");
                    else
                        interactionLogger.Information("Command refused: {Reason}", message);
                    break;
                case OperationCanceledException:
                    message = "Command processing was cancelled.";
                    interactionLogger.Information("Command processing was cancelled");
                    break;
                case not null:
                    message = GenericFailureMessage;
                    interactionLogger.Error(exception, "Unexpected command failure");
                    break;
                default:
                    message = result.Error switch
                    {
                        InteractionCommandError.UnmetPrecondition => string.IsNullOrWhiteSpace(result.ErrorReason)
                            ? "Command precondition failed."
                            : $"Command failed: {result.ErrorReason}",
                        InteractionCommandError.UnknownCommand => "That command is unavailable.",
                        InteractionCommandError.BadArgs or InteractionCommandError.ParseFailed
                            or InteractionCommandError.ConvertFailed => "The command arguments could not be understood.",
                        _ => GenericFailureMessage
                    };
                    interactionLogger.Information("Command failed: {CommandError}", result.Error);
                    break;
            }

            await RespondToFailureAsync(context.Interaction, message);
        }
        catch (Exception ex)
        {
            interactionLogger
                .ForContext("event_kind", "interaction.response_failed")
                .Warning(ex, "Failed to complete interaction error handling");
        }
    }

    private static async Task RespondToFailureAsync(IDiscordInteraction interaction, string message)
    {
        if (!interaction.HasResponded)
        {
            await interaction.RespondAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        IUserMessage original;
        try
        {
            original = await interaction.GetOriginalResponseAsync();
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            await interaction.FollowupAsync(GenericFailureMessage, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var sourceId = interaction switch
        {
            IComponentInteraction component => component.Message?.Id,
            IModalInteraction modal => modal.Message?.Id,
            _ => null
        };
        var requiresOwnershipCheck = interaction is IComponentInteraction or IModalInteraction;
        var owned = !requiresOwnershipCheck || (sourceId.HasValue && sourceId.Value != original.Id);
        var flags = original.Flags ?? MessageFlags.None;
        var ephemeral = flags.HasFlag(MessageFlags.Ephemeral);
        var loading = flags.HasFlag(MessageFlags.Loading);
        var plainText = !flags.HasFlag(MessageFlags.ComponentsV2)
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

        await interaction.FollowupAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private void LogRegisteredInteractionCommands()
    {
        ModuleInfo[] modules = EnumerateModules(interactionService.Modules).ToArray();

        var moduleDetails = modules
            .Select(module => new
            {
                module.Name,
                module.SlashGroupName,
                SlashCommandCount = module.SlashCommands.Count,
                ContextCommandCount = module.ContextCommands.Count,
                ComponentCommandCount = module.ComponentCommands.Count,
                ModalCommandCount = module.ModalCommands.Count
            })
            .ToArray();

        List<object> commandDetails = [];

        foreach (var module in modules)
        {
            commandDetails.AddRange(
                module.SlashCommands.Select(command => new
                {
                    Type = "Slash",
                    Module = module.Name,
                    SlashGroup = module.SlashGroupName,
                    command.Name,
                    Method = command.MethodName,
                    SupportsWildCards = (bool?)null,
                    ModalType = (string?)null
                })
            );

            commandDetails.AddRange(
                module.ContextCommands.Select(command => new
                {
                    Type = "Context",
                    Module = module.Name,
                    SlashGroup = (string?)null,
                    command.Name,
                    Method = command.MethodName,
                    SupportsWildCards = (bool?)null,
                    ModalType = (string?)null
                })
            );

            commandDetails.AddRange(
                module.ComponentCommands.Select(command => new
                {
                    Type = "Component",
                    Module = module.Name,
                    SlashGroup = (string?)null,
                    command.Name,
                    Method = command.MethodName,
                    SupportsWildCards = (bool?)command.SupportsWildCards,
                    ModalType = (string?)null
                })
            );

            commandDetails.AddRange(
                module.ModalCommands.Select(command => new
                {
                    Type = "Modal",
                    Module = module.Name,
                    SlashGroup = (string?)null,
                    command.Name,
                    Method = command.MethodName,
                    SupportsWildCards = (bool?)command.SupportsWildCards,
                    ModalType = command.Modal.Type.FullName
                })
            );
        }

        _logger.Information(
            "Registered interaction commands. SlashCommandCount={SlashCommandCount} ContextCommandCount={ContextCommandCount} ComponentCommandCount={ComponentCommandCount} ModalCommandCount={ModalCommandCount} ModuleCount={ModuleCount}",
            interactionService.SlashCommands.Count,
            interactionService.ContextCommands.Count,
            interactionService.ComponentCommands.Count,
            interactionService.ModalCommands.Count,
            moduleDetails.Length
        );

        _logger.Information(
            "Registered interaction command details. Modules={@InteractionModules} Commands={@InteractionCommands}",
            moduleDetails,
            commandDetails
        );
    }

    private async Task RegisterCommandsAsync()
    {
        _logger.Information("Registering application commands in {GuildCount} guilds", discordClient.Guilds.Count);

        foreach (var guild in discordClient.Guilds)
            await RegisterCommandsToGuildAsync(guild);
    }

    private async Task RegisterCommandsToGuildAsync(SocketGuild guild)
    {
        try
        {
            await interactionService.RegisterCommandsToGuildAsync(guild.Id, true);
            _logger.Information("Application commands registered to guild {GuildId}", guild.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to register application commands to guild {GuildId}", guild.Id);
        }
    }

    internal readonly record struct CommandUsageDetails(
        string CommandName,
        ulong? InvokeeUserId,
        string? InvokeeUsername,
        string? InvokeeSource);
}