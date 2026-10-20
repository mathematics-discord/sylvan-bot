namespace RatBot.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RatBot.Features.EmojiAnalytics;
using RatBot.Features.Logging;
using RatBot.Features.Moderation;
using RatBot.Features.RoleColours;
using RatBot.Gateway;
using Serilog;

public sealed class DependencyInjectionTests
{
    [Test]
    public async Task BuildServiceProvider_RealServiceGraph_HasNoCaptiveScopedDependencies()
    {
        // Arrange
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["DB:ConnectionString"] = "Host=localhost;Database=ratbot_di_test;Username=ratbot;Password=unused",
                    ["Discord:Token"] = "inert-test-token",
                    ["Discord:MessageCacheSize"] = "1000"
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<ILogger>(new LoggerConfiguration().CreateLogger());
        services.AddRatBot(configuration);

        // Act
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );

        IDiscordGatewayHandler[] gatewayHandlers = provider.GetServices<IDiscordGatewayHandler>().ToArray();
        await using var scope = provider.CreateAsyncScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ImageSpamSettingsService>();

        // Assert
        settingsService.ShouldNotBeNull();
        gatewayHandlers.ShouldContain(handler => handler is ModerationLoggingGatewayHandler);
        gatewayHandlers.ShouldContain(handler => handler is RoleColourGatewayHandler);
        gatewayHandlers.ShouldContain(handler => handler is EmojiAnalyticsGatewayHandler);
    }

    [Test]
    public void DiscordOptions_DoNotRequireOrExposeARuntimeGuildAllowlist()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Discord:Token"] = "inert-test-token",
                    ["Discord:MessageCacheSize"] = "1000"
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<DiscordOptions>().Bind(configuration.GetSection(DiscordOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DiscordOptions>>().Value;

        options.Token.ShouldBe("inert-test-token");
        typeof(DiscordOptions).GetProperty("CommandRegistrationGuildIds").ShouldBeNull();
        typeof(DiscordOptions).GetProperty("GuildId").ShouldBeNull();
    }
}
