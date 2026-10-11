using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using RatBot.Application.Moderation;
using RatBot.Commands.Settings;
using RatBot.Commands.Spam;
using RatBot.Domain.Moderation;
using Shouldly;

namespace RatBot.Discord.Tests.Commands.Settings;

[TestFixture]
public sealed class SettingsInteractionRegistrationTests
{
    private static void AssertMetaCommand(string methodName, string commandName, Type parameterType)
    {
        MethodInfo method =
            typeof(SettingsModule.MetaSettingsModule).GetMethod(methodName) ??
            throw new InvalidOperationException($"Expected {methodName} method.");

        method.GetParameters().Single().ParameterType.ShouldBe(parameterType);

        SlashCommandAttribute slashCommand =
            method.GetCustomAttribute<SlashCommandAttribute>() ??
            throw new InvalidOperationException("Expected slash command attribute.");

        slashCommand.Name.ShouldBe(commandName);
    }

    [Test]
    public async Task ImageSpamConfigAsync_CanBeRegisteredByInteractionService()
    {
        // Arrange
        DiscordSocketClient client = new DiscordSocketClient();
        InteractionService interactionService = new InteractionService(client);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<ImageBurstSpamDetectorSettings>()
            .AddSingleton<IImageSpamSettingsStore, FakeImageSpamSettingsStore>()
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

    [Test]
    public void MetaSettingsCommands_HaveExpectedParameters()
    {
        AssertMetaCommand(nameof(SettingsModule.MetaSettingsModule.SetSuggestionsForumChannelAsync), "suggestions",
            typeof(IForumChannel));

        AssertMetaCommand(nameof(SettingsModule.MetaSettingsModule.SetProposalsForumChannelAsync), "proposals",
            typeof(IForumChannel));

        AssertMetaCommand(nameof(SettingsModule.MetaSettingsModule.SetCabinetRoleAsync), "cabinet", typeof(IRole));

        AssertMetaCommand(nameof(SettingsModule.MetaSettingsModule.SetCabinetChairRoleAsync), "chair", typeof(IRole));

        AssertMetaCommand(nameof(SettingsModule.MetaSettingsModule.SetCommitteeRoleAsync), "committee", typeof(IRole));
    }

    [Test]
    public void MetaSettingsModule_HasExpectedGroupMetadata()
    {
        // Arrange
        Type moduleType = typeof(SettingsModule.MetaSettingsModule);

        // Act
        GroupAttribute group =
            moduleType.GetCustomAttribute<GroupAttribute>() ??
            throw new InvalidOperationException("Expected meta group attribute.");

        // Assert
        group.Name.ShouldBe("meta");
        group.Description.ShouldBe("Meta configuration.");
    }

    private sealed class FakeImageSpamSettingsStore : IImageSpamSettingsStore
    {
        public Task<ImageSpamSettings?> GetAsync(ulong guildId, CancellationToken ct)
        {
            return Task.FromResult<ImageSpamSettings?>(null);
        }

        public Task<IReadOnlyList<ImageSpamSettings>> ListEnabledAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<ImageSpamSettings>>([]);
        }

        public Task<ImageSpamSettings> UpsertAsync(
            ulong guildId,
            int? requiredChannelCount,
            int? requiredAttachmentCount,
            int? burstDurationSeconds,
            CancellationToken ct
        )
        {
            return Task.FromResult(ImageSpamSettings.CreateDefault(guildId));
        }
    }
}