using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SylvanBot.Gateway;

namespace SylvanBot.Tests;

public sealed class DependencyInjectionTests
{
    [Test]
    public void DiscordOptions_DoNotRequireOrExposeARuntimeGuildAllowlist()
    {
        IConfiguration configuration = new ConfigurationBuilder()
           .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Discord:Token"]            = "inert-test-token",
                    ["Discord:MessageCacheSize"] = "1000",
                }
            )
           .Build();

        var services = new ServiceCollection();
        services.AddOptions<DiscordOptions>().Bind(configuration.GetSection(DiscordOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var       options  = provider.GetRequiredService<IOptions<DiscordOptions>>().Value;

        options.Token.ShouldBe("inert-test-token");
        typeof(DiscordOptions).GetProperty("CommandRegistrationGuildIds").ShouldBeNull();
        typeof(DiscordOptions).GetProperty("GuildId").ShouldBeNull();
    }
}