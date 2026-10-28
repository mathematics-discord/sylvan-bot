namespace SylvanBot.Interactions;

public sealed class InteractionDiagnostics(ILogger logger)
{
    private const string DiagEventName = "interaction_diagnostics";
    private readonly ILogger _logger = logger.ForContext<DiscordInteractionHandler>();
    private readonly string _serviceInstanceId = Environment.MachineName;

    private static IEnumerable<SocketSlashCommandDataOption> EnumerateSlashOptions(IReadOnlyCollection<SocketSlashCommandDataOption> options)
    {
        foreach (var option in options)
        {
            yield return option;

            foreach (var nested in EnumerateSlashOptions(option.Options))
                yield return nested;
        }
    }

    public static CommandUsageDetails GetCommandUsageDetails(SocketInteraction interaction)
    {
        return interaction switch
        {
            SocketSlashCommand slashCommand => GetSlashUsageDetails(slashCommand),
            SocketUserCommand userCommand => GetUserContextUsageDetails(userCommand),
            SocketMessageCommand messageCommand => GetMessageContextUsageDetails(messageCommand),
            _ => new CommandUsageDetails(GetInteractionName(interaction), InvokeeUserId: null, InvokeeUsername: null, InvokeeSource: null),
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
            _ => interaction.Type.ToString(),
        };
    }

    private static CommandUsageDetails GetMessageContextUsageDetails(SocketMessageCommand messageCommand)
    {
        IUser? invokee = messageCommand.Data.Message.Author;

        return new CommandUsageDetails(
            CommandName: messageCommand.Data.Name,
            InvokeeUserId: invokee?.Id,
            InvokeeUsername: invokee?.Username,
            "message_context_author");
    }

    private static string GetSlashCommandName(SocketSlashCommand command)
    {
        List<string> parts = [command.Data.Name];
        IReadOnlyCollection<SocketSlashCommandDataOption> options = command.Data.Options;

        while (true)
        {
            var subCommandOption = options.FirstOrDefault(option =>
                option.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup);

            if (subCommandOption is null)
                return string.Join(" ", values: parts);

            parts.Add(subCommandOption.Name);
            options = subCommandOption.Options;
        }
    }

    private static CommandUsageDetails GetSlashUsageDetails(SocketSlashCommand slashCommand)
    {
        var commandName = GetSlashCommandName(slashCommand);

        IEnumerable<SocketSlashCommandDataOption> options = EnumerateSlashOptions(slashCommand.Data.Options);

        foreach (var option in options)
        {
            if (option.Type is not (ApplicationCommandOptionType.User or ApplicationCommandOptionType.Mentionable))
                continue;

            switch (option.Value)
            {
                case SocketGuildUser guildUser:
                    return new CommandUsageDetails(
                        CommandName: commandName,
                        InvokeeUserId: guildUser.Id,
                        InvokeeUsername: guildUser.Username,
                        $"slash_option:{option.Name}");
                case SocketUser socketUser:
                    return new CommandUsageDetails(
                        CommandName: commandName,
                        InvokeeUserId: socketUser.Id,
                        InvokeeUsername: socketUser.Username,
                        $"slash_option:{option.Name}");
                case IUser user:
                    return new CommandUsageDetails(
                        CommandName: commandName,
                        InvokeeUserId: user.Id,
                        InvokeeUsername: user.Username,
                        $"slash_option:{option.Name}");
                case ulong userId:
                    return new CommandUsageDetails(
                        CommandName: commandName,
                        InvokeeUserId: userId,
                        InvokeeUsername: null,
                        $"slash_option:{option.Name}");
            }
        }

        return new CommandUsageDetails(CommandName: commandName, InvokeeUserId: null, InvokeeUsername: null, InvokeeSource: null);
    }

    private static CommandUsageDetails GetUserContextUsageDetails(SocketUserCommand userCommand)
    {
        var invokee = userCommand.Data.Member ?? ((IUserCommandInteractionData)userCommand.Data).User;

        return new CommandUsageDetails(
            CommandName: userCommand.Data.Name,
            InvokeeUserId: invokee?.Id,
            InvokeeUsername: invokee?.Username,
            "user_context_target");
    }

    public static void LogCommandUsage(ILogger interactionLogger, IInteractionContext context, CommandUsageDetails usage)
    {
        interactionLogger
            .ForContext("method_context", $"{nameof(DiscordInteractionHandler)}.{nameof(LogCommandUsage)}")
            .ForContext("event_kind", "interaction.command_invoked")
            .ForContext("component", "interaction_dispatch")
            .ForContext("outcome", "received")
            .ForContext("command_name", value: usage.CommandName)
            .ForContext("invoker_user_id", value: context.User.Id)
            .ForContext("invokee_user_id", value: usage.InvokeeUserId)
            .ForContext("invokee_username", value: usage.InvokeeUsername)
            .ForContext("invokee_source", value: usage.InvokeeSource)
            .Debug("Command invoked");
    }

    public ILogger CreateInteractionDiagnosticsLogger(IInteractionContext context)
    {
        var interactionLogger = _logger
            .ForContext("diag_event", value: DiagEventName)
            .ForContext("service_instance_id", value: _serviceInstanceId)
            .ForContext("process_id", value: Environment.ProcessId)
            .ForContext("interaction_id", value: context.Interaction.Id)
            .ForContext("interaction_type", context.Interaction.Type.ToString());

        return context.Interaction is SocketInteraction socketInteraction
            ? interactionLogger
                .ForContext("interaction_name", GetInteractionName(socketInteraction))
                .ForContext("interaction_created_at_utc", socketInteraction.CreatedAt.UtcDateTime.ToString("O"))
            : interactionLogger;
    }

    public InteractionLogScopeDetails CreateInteractionLogScopeDetails(
        SocketInteraction interaction,
        IInteractionContext context,
        CommandUsageDetails usage
    ) =>
        new InteractionLogScopeDetails(
            ServiceInstanceId: _serviceInstanceId,
            ProcessId: Environment.ProcessId,
            InteractionId: interaction.Id,
            interaction.Type.ToString(),
            GetInteractionName(interaction),
            interaction.CreatedAt.UtcDateTime.ToString("O"),
            UserId: context.User.Id,
            GuildId: context.Guild?.Id,
            ChannelId: context.Channel?.Id,
            interaction.Type == InteractionType.ApplicationCommand ? usage.CommandName : null);

    public readonly record struct CommandUsageDetails(string CommandName, ulong? InvokeeUserId, string? InvokeeUsername, string? InvokeeSource);
}