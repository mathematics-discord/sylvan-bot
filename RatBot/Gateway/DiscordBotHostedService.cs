namespace RatBot.Gateway;

using Serilog.Events;

public sealed class DiscordBotHostedService : IHostedService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IEnumerable<IDiscordGatewayHandler> _gatewayHandlers;
    private readonly ILogger _logger;
    private readonly DiscordOptions _options;

    public DiscordBotHostedService(
        DiscordSocketClient discordClient,
        InteractionService interactionService,
        IEnumerable<IDiscordGatewayHandler> gatewayHandlers,
        IOptions<DiscordOptions> options,
        ILogger logger
    )
    {
        _discordClient = discordClient;
        _gatewayHandlers = gatewayHandlers;
        _options = options.Value;
        _logger = logger.ForContext<DiscordBotHostedService>();

        _discordClient.Log += message =>
        {
            LogDiscordMessage("Gateway", message);
            return Task.CompletedTask;
        };

        interactionService.Log += message =>
        {
            if (message.Exception is not Discord.Interactions.InteractionException)
                LogDiscordMessage("Interactions", message);
            return Task.CompletedTask;
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _discordClient.Connected += OnConnectedAsync;
        _discordClient.Disconnected += OnDisconnectedAsync;
        _discordClient.GuildMembersDownloaded += OnGuildMembersDownloadedAsync;

        foreach (var handler in _gatewayHandlers)
            await handler.InitializeAsync(cancellationToken);

        await _discordClient.LoginAsync(TokenType.Bot, _options.Token);
        await _discordClient.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var handler in _gatewayHandlers.Reverse())
            handler.Unsubscribe();

        _discordClient.Connected -= OnConnectedAsync;
        _discordClient.Disconnected -= OnDisconnectedAsync;
        _discordClient.GuildMembersDownloaded -= OnGuildMembersDownloadedAsync;

        await _discordClient.StopAsync();
        await _discordClient.LogoutAsync();
    }

    private void LogDiscordMessage(string category, LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogEventLevel.Fatal,
            LogSeverity.Error => LogEventLevel.Error,
            LogSeverity.Warning => LogEventLevel.Warning,
            LogSeverity.Info => LogEventLevel.Information,
            LogSeverity.Verbose => LogEventLevel.Verbose,
            _ => LogEventLevel.Debug,
        };

        var source = string.IsNullOrWhiteSpace(message.Source) ? "Unknown" : message.Source;

        if (message.Exception is not null)
        {
            _logger.Write(level, message.Exception, "[{Category}] {Source}: {Message}", category, source,
                message.Message);

            return;
        }

        _logger.Write(level, "[{Category}] {Source}: {Message}", category, source, message.Message);
    }

    private Task OnConnectedAsync()
    {
        _logger.Information("Gateway connected");
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
            _logger.Warning("Gateway disconnected");
        else
            _logger.Warning(exception, "Gateway disconnected with error");

        return Task.CompletedTask;
    }

    private Task OnGuildMembersDownloadedAsync(SocketGuild guild)
    {
        _logger.Information(
            "Guild member cache populated. GuildId={GuildId}, DownloadedMemberCount={DownloadedMemberCount}, MemberCount={MemberCount}, HasAllMembers={HasAllMembers}",
            guild.Id,
            guild.DownloadedMemberCount,
            guild.MemberCount,
            guild.HasAllMembers
        );

        return Task.CompletedTask;
    }
}