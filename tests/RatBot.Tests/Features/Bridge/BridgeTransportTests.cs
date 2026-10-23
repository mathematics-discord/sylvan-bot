namespace RatBot.Tests.Features.Bridge;

using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RatBot.Features.Bridge;
using RatBot.Infrastructure.Data;
using Serilog;

/// <summary>Exercises the real endpoint and WebSocket client without logging in to Discord.</summary>
public sealed class BridgeTransportTests
{
    private const string Credential = "bridge-local-test-credential-000000000000";
    private WebApplication _app = null!;
    private Uri _socketUri = null!;
    private CancellationTokenSource _timeout = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton<IOptions<BridgeOptions>>(Options.Create(new BridgeOptions { Credential = Credential }));
        builder.Services.AddSingleton<Serilog.ILogger>(new LoggerConfiguration().CreateLogger());
        builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
            AlwaysDownloadUsers = false
        }));

        // This provider is never opened: endpoint-only checks need no stored channels.
        builder.Services.AddDbContextFactory<BotDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused"));

        builder.Services.AddSingleton<BridgeChannels>();
        builder.Services.AddSingleton<BridgeEndpoint>();
        _app = builder.Build();
        _app.UseWebSockets();
        _app.Run(_app.Services.GetRequiredService<BridgeEndpoint>().HandleAsync);
        await _app.StartAsync(_timeout.Token);
        var server = _app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _socketUri = new Uri(address.Replace("http:", "ws:", StringComparison.Ordinal) + "/bridge");
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await _app.StopAsync(_timeout.Token);
        await _app.DisposeAsync();
        _timeout.Dispose();
    }

    [TestCase(null)]
    [TestCase("incorrect-credential")]
    public async Task Connect_WithoutValidCredential_RejectsUpgrade(string? credential)
    {
        // Arrange
        using var client = new ClientWebSocket();
        client.Options.CollectHttpResponseDetails = true;

        if (credential is not null)
            client.Options.SetRequestHeader("Authorization", "Bearer " + credential);

        // Act
        await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectAsync(_socketUri, _timeout.Token));

        // Assert
        client.HttpStatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Connect_WhileClientActive_RejectsSecondClientAndKeepsFirstUsable()
    {
        // Arrange
        using var first = CreateClient();
        await first.ConnectAsync(_socketUri, _timeout.Token);
        var confirmation = await ReceiveAsync(first);
        using var second = CreateClient();

        // Act
        await Assert.ThrowsAsync<WebSocketException>(() => second.ConnectAsync(_socketUri, _timeout.Token));
        await SendAsync(first, "{\"version\":1,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":\"123\"}");
        var result = await ReceiveAsync(first);

        // Assert
        confirmation.GetProperty("type").GetString().ShouldBe("connected");
        second.HttpStatusCode.ShouldBe(HttpStatusCode.Conflict);
        result.GetProperty("code").GetString().ShouldBe("unknown_origin");
        result.GetProperty("requestId").GetString().ShouldBe("r1");
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", _timeout.Token);
    }

    [Test]
    public async Task Receive_InvalidDuplicateAndReconnectedActions_PreservesConnectionScope()
    {
        // Arrange
        const string action = "{\"version\":1,\"type\":\"reply\",\"requestId\":\"r1\",\"messageId\":\"123\",\"text\":\"Hello\"}";
        using var first = CreateClient();
        await first.ConnectAsync(_socketUri, _timeout.Token);
        await ReceiveAsync(first);

        // Act
        await SendAsync(first, "{\"version\":2,\"type\":\"typing\",\"requestId\":\"bad\",\"messageId\":\"123\"}");
        var invalid = await ReceiveAsync(first);
        await SendAsync(first, action);
        var unknown = await ReceiveAsync(first);
        await SendAsync(first, action);
        var duplicate = await ReceiveAsync(first);
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", _timeout.Token);

        using var next = CreateClient();
        await next.ConnectAsync(_socketUri, _timeout.Token);
        await ReceiveAsync(next);
        await SendAsync(next, action);
        var reconnected = await ReceiveAsync(next);

        // Assert
        invalid.GetProperty("code").GetString().ShouldBe("invalid_frame");
        unknown.GetProperty("code").GetString().ShouldBe("unknown_origin");
        duplicate.GetProperty("code").GetString().ShouldBe("duplicate_request");
        reconnected.GetProperty("code").GetString().ShouldBe("unknown_origin");
        await next.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", _timeout.Token);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Receive_BinaryOrOversizedFrame_TerminatesConnection(bool oversized)
    {
        // Arrange
        using var client = CreateClient();
        await client.ConnectAsync(_socketUri, _timeout.Token);
        await ReceiveAsync(client);
        var data = oversized ? new byte[BridgeProtocol.MaxFrameBytes + 1] : new byte[1];
        var type = oversized ? WebSocketMessageType.Text : WebSocketMessageType.Binary;

        // Act
        await client.SendAsync(data.AsMemory(), type, true, _timeout.Token);

        // Assert
        await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            var buffer = new byte[128];
            await client.ReceiveAsync(buffer.AsMemory(), _timeout.Token);
        });
    }

    [Test]
    public async Task Stop_WithActiveClient_CleansUpConnection()
    {
        // Arrange
        using var client = CreateClient();
        await client.ConnectAsync(_socketUri, _timeout.Token);
        await ReceiveAsync(client);
        var endpoint = _app.Services.GetRequiredService<BridgeEndpoint>();

        // Act
        await _app.StopAsync(_timeout.Token);

        // Assert
        endpoint.Connection.ShouldBeNull();
    }

    private static ClientWebSocket CreateClient()
    {
        var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Authorization", "Bearer " + Credential);
        client.Options.CollectHttpResponseDetails = true;
        return client;
    }

    private async Task SendAsync(ClientWebSocket client, string text)
    {
        await client.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(), WebSocketMessageType.Text, true, _timeout.Token);
    }

    private async Task<JsonElement> ReceiveAsync(ClientWebSocket client)
    {
        var buffer = new byte[65536];
        var count = 0;
        ValueWebSocketReceiveResult result;

        do
        {
            result = await client.ReceiveAsync(buffer.AsMemory(count), _timeout.Token);
            count += result.Count;
        }
        while (!result.EndOfMessage);

        using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
        return json.RootElement.Clone();
    }
}