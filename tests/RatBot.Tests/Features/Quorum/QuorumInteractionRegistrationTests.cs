using System.Reflection;
using Discord.Interactions;
using Discord.WebSocket;
using Discord;
using Microsoft.Extensions.DependencyInjection;
using RatBot.Features.Quorum.Commands;
using RatBot.Features.Quorum.Persistence;
using Shouldly;

namespace RatBot.Tests.Features.Quorum;

[TestFixture]
public sealed class QuorumInteractionRegistrationTests
{
    private static void AssertGroup(Type moduleType, string expectedName, GuildPermission expectedPermission)
    {
        GroupAttribute group = moduleType.GetCustomAttribute<GroupAttribute>() ??
                               throw new InvalidOperationException("Expected group attribute.");

        DefaultMemberPermissionsAttribute permissions =
            moduleType.GetCustomAttribute<DefaultMemberPermissionsAttribute>()
            ?? throw new InvalidOperationException("Expected default permissions attribute.");

        group.Name.ShouldBe(expectedName);
        permissions.Permissions.ShouldBe(expectedPermission);
    }

    [Test]
    public async Task InteractionService_RegistersFinalQuorumCommandSurface()
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(new QuorumConfigurationStore("unused"))
            .AddSingleton<Serilog.ILogger>(new Serilog.LoggerConfiguration().CreateLogger())
            .BuildServiceProvider();

        InteractionService interactionService = new InteractionService(
            new DiscordSocketClient(),
            new InteractionServiceConfig { AutoServiceScopes = true }
        );

        ModuleInfo admin = await interactionService.AddModuleAsync<QuorumAdminModule>(services);
        ModuleInfo quorum = await interactionService.AddModuleAsync<QuorumModule>(services);

        admin.SlashGroupName.ShouldBe("quorum-admin");
        admin.SlashCommands
            .Select(command => command.Name)
            .ShouldBe(["register", "role", "remove"], true);

        quorum.SlashGroupName.ShouldBe("quorum");
        quorum.SlashCommands
            .Select(command => command.Name)
            .ShouldBe(["inspect", "calculate"], true);
    }

    [Test]
    public void Modules_HaveExpectedGroupPermissions()
    {
        AssertGroup(typeof(QuorumAdminModule), "quorum-admin", GuildPermission.Administrator);
        AssertGroup(typeof(QuorumModule), "quorum", GuildPermission.MuteMembers);
    }

    [Test]
    public void RoleAsync_UsesDiscordRoleAndDefaultsShouldAddToTrue()
    {
        MethodInfo method =
            typeof(QuorumAdminModule).GetMethod(nameof(QuorumAdminModule.RoleAsync))
            ?? throw new InvalidOperationException("Expected RoleAsync method.");

        ParameterInfo[] parameters = method.GetParameters();

        parameters
            .Select(parameter => parameter.ParameterType)
            .ShouldBe([typeof(IChannel), typeof(IRole), typeof(bool)]);
        parameters[2].GetCustomAttribute<SummaryAttribute>()?.Name.ShouldBe("should_add");
        parameters[2].HasDefaultValue.ShouldBeTrue();
        parameters[2].DefaultValue.ShouldBe(true);
    }
}