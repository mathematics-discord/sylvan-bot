using Microsoft.EntityFrameworkCore;
using Serilog.Events;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Gateway;

public sealed class DiscordBotHostedService : IHostedService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IEnumerable<IDiscordGatewayHandler> _gatewayHandlers;
    private readonly ILogger _logger;
    private readonly DiscordOptions _options;
    private readonly MessageSnapshotCache _cache;
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    public DiscordBotHostedService(
        DiscordSocketClient discordClient,
        InteractionService interactionService,
        IEnumerable<IDiscordGatewayHandler> gatewayHandlers,
        IOptions<DiscordOptions> options,
        MessageSnapshotCache cache,
        IDbContextFactory<BotDbContext> dbContextFactory,
        ILogger logger
    )
    {
        _discordClient = discordClient;
        _gatewayHandlers = gatewayHandlers;
        _options = options.Value;
        _logger = logger.ForContext<DiscordBotHostedService>();
        _cache = cache;
        _dbContextFactory = dbContextFactory;

        _discordClient.Log += message =>
        {
            LogDiscordMessage("Gateway", message: message);

            return Task.CompletedTask;
        };

        interactionService.Log += message =>
        {
            if (message.Exception is not InteractionException)
                LogDiscordMessage("Interactions", message: message);

            return Task.CompletedTask;
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _discordClient.Connected += OnConnectedAsync;
        _discordClient.Disconnected += OnDisconnectedAsync;
        _discordClient.GuildMembersDownloaded += OnGuildMembersDownloadedAsync;

        await _discordClient.LoginAsync(tokenType: TokenType.Bot, token: _options.Token);

        try
        {
            var bot = await _discordClient.Rest.GetApplicationInfoAsync(new RequestOptions { CancelToken = cancellationToken });

            IEnumerable<Discord.Rest.RestUserGuild> guilds = await _discordClient
                .Rest.GetGuildSummariesAsync(options: new RequestOptions { CancelToken = cancellationToken })
                .FlattenAsync();

            var guildIds = guilds.Select(guild => guild.Id).ToArray();

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var configuredGuilds = await db
                .LoggingConfigurations.AsNoTracking()
                .Where(configuration => configuration.Enabled && guildIds.Contains(configuration.GuildId))
                .Select(configuration => configuration.GuildId)
                .ToArrayAsync(cancellationToken);

            await _cache.InitializeAsync(bot.Id, configuredGuilds.ToImmutableArray(), cancellationToken);
            _logger.Information("Message cache hydrated for bot {BotId} across {GuildCount} configured guilds", bot.Id, configuredGuilds.Length);
        }
        catch (Exception exception)
        {
            _logger.Error("Message cache hydration failed: {ExceptionType}; gateway startup aborted", exception.GetType().Name);

            throw;
        }

        foreach (var handler in _gatewayHandlers)
            await handler.InitializeAsync(cancellationToken);

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
            _logger.Write(
                level: level,
                exception: message.Exception,
                "[{Category}] {Source}: {Message}",
                propertyValue0: category,
                propertyValue1: source,
                propertyValue2: message.Message
            );

            return;
        }

        _logger.Write(
            level: level,
            "[{Category}] {Source}: {Message}",
            propertyValue0: category,
            propertyValue1: source,
            propertyValue2: message.Message
        );
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
            _logger.Warning(exception: exception, "Gateway disconnected with error");

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
