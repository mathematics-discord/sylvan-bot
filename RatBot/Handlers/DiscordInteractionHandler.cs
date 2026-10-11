using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Discord.Net;
using RatBot.Commands.Hello;
using RatBot.Gateway;
using RatBot.Logging;
using IResult = Discord.Interactions.IResult;

namespace RatBot.Handlers;

public sealed class DiscordInteractionHandler(
    DiscordSocketClient discordClient,
    InteractionService interactionService,
    IServiceProvider services,
    ILogger logger) : IDiscordGatewayHandler
{
    private const string DiagEventName = "interaction_diagnostics";
    private readonly ConcurrentDictionary<ulong, Stopwatch> _interactionStopwatches = [];
    private readonly ILogger _logger = logger.ForContext<DiscordInteractionHandler>();

    private readonly string _serviceInstanceId = Environment.MachineName;

    private static IEnumerable<ModuleInfo> EnumerateModules(IEnumerable<ModuleInfo> modules)
    {
        foreach (ModuleInfo module in modules)
        {
            yield return module;

            foreach (ModuleInfo subModule in EnumerateModules(module.SubModules))
                yield return subModule;
        }
    }

    private static IEnumerable<SocketSlashCommandDataOption> EnumerateSlashOptions(
        IReadOnlyCollection<SocketSlashCommandDataOption> options)
    {
        foreach (SocketSlashCommandDataOption option in options)
        {
            yield return option;

            foreach (SocketSlashCommandDataOption nested in EnumerateSlashOptions(option.Options))
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
            SocketSlashCommandDataOption? subCommandOption = options.FirstOrDefault(option =>
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
        string commandName = GetSlashCommandName(slashCommand);

        foreach (SocketSlashCommandDataOption option in EnumerateSlashOptions(slashCommand.Data.Options))
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
        IUser? invokee = userCommand.Data.Member ?? ((IUserCommandInteractionData)userCommand.Data).User;
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

    private static async Task TryRespondToUnmetPreconditionAsync(IInteractionContext context, IResult result,
        ILogger interactionLogger)
    {
        if (result.Error != InteractionCommandError.UnmetPrecondition)
            return;

        if (context.Interaction.HasResponded)
            return;

        string reason = string.IsNullOrWhiteSpace(result.ErrorReason)
            ? "Command precondition failed."
            : result.ErrorReason;

        try
        {
            await context.Interaction.RespondAsync($"Command failed: {reason}", ephemeral: true);

            interactionLogger
                .ForContext("diag_stage", "precondition_response")
                .ForContext("diag_outcome", "success")
                .ForContext("event_kind", "interaction.precondition_response")
                .ForContext("outcome", "success")
                .Debug("interaction_diag has_responded={HasResponded}", context.Interaction.HasResponded);
        }
        catch (HttpException ex) when (ex.DiscordCode == (DiscordErrorCode)10062)
        {
            interactionLogger.Warning(ex, "Unknown interaction while responding to unmet precondition");
        }
        catch (HttpException ex) when (ex.DiscordCode == (DiscordErrorCode)40060)
        {
            interactionLogger.Information(ex,
                "Interaction was already acknowledged while responding to unmet precondition");
        }
        catch (Exception ex)
        {
            interactionLogger.Warning(ex, "Failed to send unmet precondition response");
        }
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        Assembly interactionsAssembly = typeof(HelloModule).Assembly;
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

    private ILogger CreateInteractionDiagnosticsLogger(SocketInteraction interaction)
    {
        return _logger
            .ForContext("diag_event", DiagEventName)
            .ForContext("service_instance_id", _serviceInstanceId)
            .ForContext("process_id", Environment.ProcessId)
            .ForContext("interaction_id", interaction.Id)
            .ForContext("interaction_type", interaction.Type.ToString())
            .ForContext("interaction_name", GetInteractionName(interaction))
            .ForContext("interaction_created_at_utc", interaction.CreatedAt.UtcDateTime.ToString("O"));
    }

    private ILogger CreateInteractionDiagnosticsLogger(IInteractionContext context)
    {
        ILogger interactionLogger = _logger
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

        Stopwatch totalStopwatch = Stopwatch.StartNew();

        ILogger interactionLogger = CreateInteractionDiagnosticsLogger(interaction)
            .ForContext("diag_component", "interaction_dispatch")
            .ForContext("component", "interaction_dispatch");

        InteractionLogScope? interactionLogScope = null;

        try
        {
            IInteractionContext context = new SocketInteractionContext(discordClient, interaction);
            _interactionStopwatches[interaction.Id] = totalStopwatch;
            CommandUsageDetails usage = GetCommandUsageDetails(interaction);

            interactionLogScope =
                InteractionLogScope.Begin(CreateInteractionLogScopeDetails(interaction, context, usage));

            interactionLogger = interactionLogger
                .ForContext("user_id", context.User.Id)
                .ForContext("guild_id", context.Guild?.Id)
                .ForContext("channel_id", context.Channel?.Id);

            if (interaction.Type == InteractionType.ApplicationCommand)
                LogCommandUsage(interactionLogger, context, usage);

            IResult result = await interactionService.ExecuteCommandAsync(context, services);

            if (result.IsSuccess)
            {
                interactionLogger
                    .ForContext("diag_stage", "dispatch")
                    .ForContext("diag_outcome", "success")
                    .ForContext("event_kind", "interaction.dispatch_completed")
                    .ForContext("outcome", "success")
                    .Debug(
                        "interaction_diag dispatch_ms={DispatchMs} has_responded={HasResponded}",
                        Math.Round(totalStopwatch.Elapsed.TotalMilliseconds, 2),
                        interaction.HasResponded
                    );

                return;
            }

            interactionLogger
                .ForContext("diag_stage", "dispatch")
                .ForContext("diag_outcome", "failed")
                .ForContext("event_kind", "interaction.dispatch_completed")
                .ForContext("outcome", "failed")
                .Warning(
                    "interaction_diag command_error={CommandError} command_reason={CommandReason}",
                    result.Error?.ToString() ?? "None",
                    result.ErrorReason ?? string.Empty
                );

            _interactionStopwatches.TryRemove(interaction.Id, out _);

            string reason = string.IsNullOrWhiteSpace(result.ErrorReason)
                ? "Command execution failed."
                : result.ErrorReason;

            if (!interaction.HasResponded)
                await interaction.RespondAsync($"Command failed: {reason}", ephemeral: true);
            else
                await interaction.FollowupAsync($"Command failed: {reason}", ephemeral: true);
        }
        catch (HttpException ex) when (ex.DiscordCode == (DiscordErrorCode)10062)
        {
            _interactionStopwatches.TryRemove(interaction.Id, out _);

            interactionLogger
                .ForContext("event_kind", "interaction.dispatch_failed")
                .ForContext("diag_stage", "dispatch")
                .ForContext("diag_outcome", "unknown_interaction")
                .ForContext("outcome", "unknown_interaction")
                .Warning(ex, "Unknown interaction received by dispatch pipeline");
        }
        catch (HttpException ex) when (ex.DiscordCode == (DiscordErrorCode)40060)
        {
            _interactionStopwatches.TryRemove(interaction.Id, out _);

            interactionLogger
                .ForContext("event_kind", "interaction.dispatch_failed")
                .ForContext("diag_stage", "dispatch")
                .ForContext("diag_outcome", "already_acknowledged")
                .ForContext("outcome", "already_acknowledged")
                .Information(ex, "Interaction was already acknowledged");
        }
        catch (Exception ex)
        {
            _interactionStopwatches.TryRemove(interaction.Id, out _);

            interactionLogger
                .ForContext("event_kind", "interaction.unhandled_exception")
                .ForContext("diag_stage", "dispatch")
                .ForContext("diag_outcome", "failed")
                .ForContext("outcome", "failed")
                .Error(ex, "Unhandled exception executing interaction");

            try
            {
                if (!interaction.HasResponded)
                    await interaction.RespondAsync("An error occurred executing that command.", ephemeral: true);
                else
                    await interaction.FollowupAsync("An error occurred executing that command.", ephemeral: true);
            }
            catch (Exception followupEx)
            {
                interactionLogger
                    .ForContext("event_kind", "interaction.response_failed")
                    .ForContext("diag_stage", "error_response")
                    .ForContext("diag_outcome", "failed")
                    .ForContext("outcome", "failed")
                    .Warning(followupEx, "Failed to send interaction error response");
            }
        }
        finally
        {
            interactionLogScope?.Dispose();
        }
    }

    private async Task HandleInteractionExecutedAsync(ICommandInfo command, IInteractionContext context, IResult result)
    {
        _interactionStopwatches.TryRemove(context.Interaction.Id, out Stopwatch? stopwatch);

        double? totalMs = stopwatch is null ? null : Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);

        string commandName = context.Interaction is SocketInteraction socketInteraction
            ? GetInteractionName(socketInteraction)
            : command.Name;

        ILogger interactionLogger = CreateInteractionDiagnosticsLogger(context)
            .ForContext("diag_component", "interaction_execution")
            .ForContext("component", "interaction_execution")
            .ForContext("command_module", command.Module.Name)
            .ForContext("command_group", command.Module.SlashGroupName)
            .ForContext("command_name", commandName)
            .ForContext("command_leaf_name", command.Name)
            .ForContext("command_method", command.MethodName)
            .ForContext("command_run_mode", command.RunMode.ToString())
            .ForContext("user_id", context.User.Id)
            .ForContext("guild_id", context.Guild?.Id)
            .ForContext("channel_id", context.Channel?.Id);

        Exception? exception = result is ExecuteResult executeResult ? executeResult.Exception : null;

        if (result.IsSuccess)
        {
            interactionLogger
                .ForContext("diag_stage", "execute_complete")
                .ForContext("diag_outcome", "success")
                .ForContext("event_kind", "interaction.execution_completed")
                .ForContext("outcome", "success")
                .Debug("interaction_diag total_ms={TotalMs} has_responded={HasResponded}", totalMs,
                    context.Interaction.HasResponded);

            return;
        }

        if (exception is not null)
        {
            interactionLogger
                .ForContext("diag_stage", "execute_complete")
                .ForContext("diag_outcome", "failed")
                .ForContext("event_kind", "interaction.execution_completed")
                .ForContext("outcome", "failed")
                .Error(
                    exception,
                    "interaction_diag total_ms={TotalMs} has_responded={HasResponded} command_error={CommandError} command_reason={CommandReason}",
                    totalMs,
                    context.Interaction.HasResponded,
                    result.Error?.ToString() ?? "None",
                    result.ErrorReason ?? string.Empty
                );

            await TryRespondToUnmetPreconditionAsync(context, result, interactionLogger);
            return;
        }

        interactionLogger
            .ForContext("diag_stage", "execute_complete")
            .ForContext("diag_outcome", "failed")
            .ForContext("event_kind", "interaction.execution_completed")
            .ForContext("outcome", "failed")
            .Warning(
                "interaction_diag total_ms={TotalMs} has_responded={HasResponded} command_error={CommandError} command_reason={CommandReason}",
                totalMs,
                context.Interaction.HasResponded,
                result.Error?.ToString() ?? "None",
                result.ErrorReason ?? string.Empty
            );

        await TryRespondToUnmetPreconditionAsync(context, result, interactionLogger);
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

        foreach (ModuleInfo module in modules)
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

        foreach (SocketGuild guild in discordClient.Guilds)
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