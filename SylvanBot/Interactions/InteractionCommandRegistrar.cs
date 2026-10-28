using SylvanBot.Features.Hello;

namespace SylvanBot.Interactions;

public sealed class InteractionCommandRegistrar(
    DiscordSocketClient discordClient,
    InteractionService interactionService,
    IServiceProvider services,
    ILogger logger
)
{
    private readonly ILogger _logger = logger.ForContext<InteractionCommandRegistrar>();

    public async Task LoadModulesAsync()
    {
        await interactionService.AddModulesAsync(typeof(HelloModule).Assembly, services);
        _logger.Information("Registered {InteractionModuleCount} interaction modules", interactionService.Modules.Count);
        LogRegisteredInteractionCommands();
    }

    private static IEnumerable<ModuleInfo> EnumerateModules(IEnumerable<ModuleInfo> modules)
    {
        foreach (var module in modules)
        {
            yield return module;

            foreach (var subModule in EnumerateModules(module.SubModules))
                yield return subModule;
        }
    }

    private static IReadOnlyCollection<object> DescribeCommands(IEnumerable<ModuleInfo> modules)
    {
        List<object> commands = [];

        foreach (var module in modules)
        {
            commands.AddRange(
                module.SlashCommands
                    .Select(command =>
                        DescribeCommand("Slash", module, command, module.SlashGroupName, wildcards: null, modalType: null)));

            commands.AddRange(
                module.ContextCommands
                    .Select(command =>
                        DescribeCommand("Context", module, command, slashGroup: null, wildcards: null, modalType: null)));

            commands.AddRange(
                module.ComponentCommands
                    .Select(command =>
                        DescribeCommand("Component", module, command, slashGroup: null, command.SupportsWildCards, modalType: null)));

            commands.AddRange(
                module.ModalCommands
                    .Select(command =>
                        DescribeCommand("Modal", module, command, slashGroup: null, command.SupportsWildCards, command.Modal.Type.FullName)));
        }

        return commands;
    }

    private static object DescribeCommand(
        string type,
        ModuleInfo module,
        ICommandInfo command,
        string? slashGroup,
        bool? wildcards,
        string? modalType
    ) =>
        new
        {
            Type = type,
            Module = module.Name,
            SlashGroup = slashGroup,
            command.Name,
            Method = command.MethodName,
            SupportsWildCards = wildcards,
            ModalType = modalType,
        };

    private void LogRegisteredInteractionCommands()
    {
        ModuleInfo[] modules = EnumerateModules(interactionService.Modules)
            .ToArray();

        var moduleDetails = modules
            .Select(module => new
            {
                module.Name,
                module.SlashGroupName,
                SlashCommandCount = module.SlashCommands.Count,
                ContextCommandCount = module.ContextCommands.Count,
                ComponentCommandCount = module.ComponentCommands.Count,
                ModalCommandCount = module.ModalCommands.Count,
            })
            .ToArray();

        IReadOnlyCollection<object> commandDetails = DescribeCommands(modules);

        _logger.Information(
            "Registered interaction commands. SlashCommandCount={SlashCommandCount} ContextCommandCount={ContextCommandCount} ComponentCommandCount={ComponentCommandCount} ModalCommandCount={ModalCommandCount} ModuleCount={ModuleCount}",
            interactionService.SlashCommands.Count,
            interactionService.ContextCommands.Count,
            interactionService.ComponentCommands.Count,
            interactionService.ModalCommands.Count,
            moduleDetails.Length);

        _logger.Information(
            "Registered interaction command details. Modules={@InteractionModules} Commands={@InteractionCommands}",
            propertyValue0: moduleDetails,
            propertyValue1: commandDetails);
    }

    public async Task RegisterCommandsAsync()
    {
        _logger.Information("Registering application commands in {GuildCount} guilds", propertyValue: discordClient.Guilds.Count);

        foreach (var guild in discordClient.Guilds)
            await RegisterCommandsToGuildAsync(guild);
    }

    public async Task RegisterCommandsToGuildAsync(SocketGuild guild)
    {
        try
        {
            await interactionService.RegisterCommandsToGuildAsync(guildId: guild.Id, deleteMissing: true);
            _logger.Information("Application commands registered to guild {GuildId}", propertyValue: guild.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(exception: ex, "Failed to register application commands to guild {GuildId}", propertyValue: guild.Id);
        }
    }
}