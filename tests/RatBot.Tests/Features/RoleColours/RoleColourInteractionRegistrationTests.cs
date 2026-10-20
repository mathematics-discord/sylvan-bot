namespace RatBot.Tests.Features.RoleColours;

using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RatBot.Features.RoleColours;
using RatBot.Infrastructure.Data;
using Serilog;

[TestFixture]
public sealed class RoleColourInteractionRegistrationTests
{
    [Test]
    public async Task RegisteringCommands()
    {
        // Arrange
        await using var client = new DiscordSocketClient();
        await using var services = new ServiceCollection()
            .AddDbContextFactory<BotDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()))
            .AddSingleton(Log.Logger)
            .AddSingleton(client)
            .AddScoped<RoleColourOperations>()
            .AddSingleton<RoleColourReconciler>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var interactions =
            new InteractionService(client, new InteractionServiceConfig { AutoServiceScopes = true });

        // Act
        var colour = await interactions.AddModuleAsync<ColourModule>(services);
        var admin = await interactions.AddModuleAsync<RoleColourAdminModule>(services);

        // Assert
        colour.SlashGroupName.ShouldBeNull();
        colour.SlashCommands.Select(command => command.Name).ShouldBe(["colour"]);

        colour.ComponentCommands.Select(command => command.Name)
            .ShouldBe(["colour-swap:select:*", "colour-swap:reset:*"], true);

        admin.SlashGroupName.ShouldBe("colour-admin");

        admin.SlashCommands.Select(command => command.Name).ShouldBe(["upsert", "delete", "list", "sync"], true);
    }
}