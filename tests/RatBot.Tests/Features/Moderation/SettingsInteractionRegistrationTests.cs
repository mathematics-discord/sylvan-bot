using System.Reflection;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RatBot.Features.Moderation;
using RatBot.Infrastructure.Data;
using Shouldly;

namespace RatBot.Tests.Features.Moderation;

[TestFixture]
public sealed class SettingsInteractionRegistrationTests
{
    [Test]
    public async Task AddModuleAsync_SpamModule_PreservesImageSpamCommandContract()
    {
        // Arrange
        await using DiscordSocketClient client = new DiscordSocketClient();
        using InteractionService interactionService = new InteractionService(client);
        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton<ImageBurstSpamDetectorSettings>()
            .AddDbContext<BotDbContext>(options =>
                options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused"))
            .AddScoped<ImageSpamSettingsService>()
            .BuildServiceProvider();

        // Act
        ModuleInfo module = await interactionService.AddModuleAsync<SpamModule>(services);
        SlashCommandInfo command = module.SlashCommands.Single();

        // Assert
        command.Name.ShouldBe("image-spam-config");
        command.Parameters.Select(parameter => parameter.Name)
            .ShouldBe(["number-of-channels", "attachment-count", "burst-duration"]);
        command.Parameters.Select(parameter => parameter.Name.Length <= 32).ShouldBe([true, true, true]);
        command.Parameters.Select(parameter => parameter.IsRequired).ShouldBe([false, false, false]);
    }

    [Test]
    public void ImageSpamConfigAsync_HasExpectedSlashCommandMetadata()
    {
        // Arrange
        MethodInfo method =
            typeof(SpamModule).GetMethod(nameof(SpamModule.ImageSpamConfigAsync))
            ?? throw new InvalidOperationException("Expected ImageSpamConfigAsync method.");

        // Act
        GroupAttribute group =
            typeof(SpamModule).GetCustomAttribute<GroupAttribute>() ??
            throw new InvalidOperationException("Expected group attribute.");

        SlashCommandAttribute slashCommand =
            method.GetCustomAttribute<SlashCommandAttribute>() ??
            throw new InvalidOperationException("Expected slash command attribute.");

        ParameterInfo[] parameters = method.GetParameters();

        // Assert
        group.Name.ShouldBe("spam");
        slashCommand.Name.ShouldBe("image-spam-config");

        parameters.Select(parameter => parameter.ParameterType).ShouldBe([typeof(int?), typeof(int?), typeof(int?)]);

        parameters
            .Select(parameter => parameter.GetCustomAttribute<SummaryAttribute>()?.Name)
            .ShouldBe(["number-of-channels", "attachment-count", "burst-duration"]);

        parameters.Select(parameter => parameter.HasDefaultValue && parameter.DefaultValue is null)
            .ShouldBe([true, true, true]);
    }

}