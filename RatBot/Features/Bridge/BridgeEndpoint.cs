namespace RatBot.Features.Bridge;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

/// <summary>Authenticates upgrades and owns the single active Bridge connection.</summary>
public sealed class BridgeEndpoint
{
    private readonly byte[] _credentialHash;
    private BridgeConnection? _connection;
    private readonly DiscordSocketClient _discordClient;
    private readonly BridgeChannels _channels;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger _logger;

    /// <summary>Authenticates upgrades and owns the single active Bridge connection.</summary>
    public BridgeEndpoint(IOptions<BridgeOptions> options,
        DiscordSocketClient discordClient,
        BridgeChannels channels,
        IHostApplicationLifetime lifetime,
        ILogger logger)
    {
        _discordClient = discordClient;
        _channels = channels;
        _lifetime = lifetime;
        _logger = logger;
        _credentialHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Credential));
    }

    public BridgeConnection? Connection => Volatile.Read(ref _connection);

    public async Task HandleAsync(HttpContext context)
    {
        if (context.Request.Path != "/bridge")
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        var authenticated = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            && CryptographicOperations.FixedTimeEquals(
                _credentialHash, SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..])));

        if (!authenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var connection = new BridgeConnection(_discordClient, _channels, _logger);

        if (Interlocked.CompareExchange(ref _connection, connection, null) is not null)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted, _lifetime.ApplicationStopping);

            await connection.RunAsync(socket, cancellation.Token);
        }
        finally
        {
            Interlocked.CompareExchange(ref _connection, null, connection);
        }
    }
}