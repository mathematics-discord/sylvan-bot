namespace SylvanBot.Features.SylvanMail;

// Owns only the member-facing connection. The unkeyed client always belongs to main Sylvan.
public sealed class SylvanMailClient : IDisposable
{
    private readonly DiscordSocketClient _main;
    private readonly SylvanMailOptions _options;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _mainReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _mailReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile SocketSelfUser? _mainReadyUser;
    private volatile SocketSelfUser? _mailReadyUser;
    private volatile bool _started;
    private int _disposed;

    public SylvanMailClient(DiscordSocketClient main, IOptions<SylvanMailOptions> options, ILogger logger)
    {
        _main = main;
        _options = options.Value;
        _logger = logger.ForContext<SylvanMailClient>();
        Client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.DirectMessages,
            AlwaysDownloadUsers = false,
            MessageCacheSize = 0,
        });

        _main.Ready += MainReadyAsync;
        _main.Disconnected += MainDisconnectedAsync;
        Client.Ready += MailReadyAsync;
        Client.Disconnected += MailDisconnectedAsync;
        Client.Log += LogAsync;
    }

    public DiscordSocketClient Client { get; }

    // Discord.NET retains the Ready session's user on RESUMED, but replaces it on a new READY.
    // ConnectionState closes admission during disconnect; a new session must finish Ready again.
    public bool IsDmReady => _started
        && Client.ConnectionState == ConnectionState.Connected
        && _mailReadyUser is not null
        && ReferenceEquals(Client.CurrentUser, _mailReadyUser);

    public bool IsReady => IsDmReady
        && _main.ConnectionState == ConnectionState.Connected
        && _mainReadyUser is not null
        && ReferenceEquals(_main.CurrentUser, _mainReadyUser);

    public void RequireDmReady()
    {
        if (!IsDmReady)
            throw new BotCommandException("SylvanMail's DM connection is unavailable.");
    }

    public void RequireReady()
    {
        if (!IsReady)
            throw new BotCommandException("Not sent: SylvanMail is disconnected or not ready. Prepare a new draft after service recovers.");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await _mainReady.Task.WaitAsync(deadline.Token);
            await Client.LoginAsync(TokenType.Bot, _options.Token).WaitAsync(deadline.Token);
            var application = await Client.Rest.GetApplicationInfoAsync(new RequestOptions { CancelToken = deadline.Token });

            if (application.Id != _options.BotId || application.Id == _main.CurrentUser.Id)
                throw new InvalidOperationException("Unexpected SylvanMail bot identity.");

            _logger.Information("SylvanMail authenticated as bot {MailBotId}; main bot {MainBotId}", application.Id, _main.CurrentUser.Id);
            await Client.StartAsync().WaitAsync(deadline.Token);
            await _mailReady.Task.WaitAsync(deadline.Token);

            if (Client.CurrentUser is not { IsBot: true } user || user.Id != _options.BotId || user.Id == _main.CurrentUser.Id)
                throw new InvalidOperationException("Unexpected SylvanMail bot identity.");

            _started = true;
            _logger.Information("SylvanMail relay ready");

            return;
        }
        catch (Exception exception)
        {
            _logger.Error("SylvanMail startup failed: {ExceptionType}", exception.GetType().Name);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await StopAsync(cleanup.Token);
        }

        // Do not retain a credential-bearing library exception in host diagnostics.
        throw new InvalidOperationException("SylvanMail startup failed; check configuration, bot identity and connectivity.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;

        try
        {
            await Client.StopAsync().WaitAsync(cancellationToken);
            await Client.LogoutAsync().WaitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.Warning("SylvanMail shutdown interrupted: {ExceptionType}", exception.GetType().Name);
        }
    }

    private Task MainReadyAsync()
    {
        _mainReadyUser = _main.CurrentUser;
        _mainReady.TrySetResult();

        return Task.CompletedTask;
    }

    private Task MailReadyAsync()
    {
        _mailReadyUser = Client.CurrentUser;
        _mailReady.TrySetResult();
        _logger.Information("SylvanMail gateway ready as bot {BotId}", Client.CurrentUser.Id);

        return Task.CompletedTask;
    }

    private Task MainDisconnectedAsync(Exception exception)
    {
        _logger.Warning("Main gateway disconnected; SylvanMail relay paused");

        return Task.CompletedTask;
    }

    private Task MailDisconnectedAsync(Exception exception)
    {
        _logger.Warning("SylvanMail gateway disconnected; relay paused");

        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage message)
    {
        // Never forward raw gateway text or exceptions, including authentication diagnostics.
        if (message.Severity <= LogSeverity.Warning)
            _logger.Warning("SylvanMail gateway diagnostic: {Severity}, {ExceptionType}", message.Severity, message.Exception?.GetType().Name);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _started = false;
        _main.Ready -= MainReadyAsync;
        _main.Disconnected -= MainDisconnectedAsync;
        Client.Ready -= MailReadyAsync;
        Client.Disconnected -= MailDisconnectedAsync;
        Client.Log -= LogAsync;
        Client.Dispose();
    }
}
