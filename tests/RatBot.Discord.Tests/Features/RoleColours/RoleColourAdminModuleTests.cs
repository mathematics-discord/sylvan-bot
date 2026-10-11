using System.Reflection;
using Discord;
using Discord.Interactions;
using RatBot.Features.RoleColours;
using Shouldly;

namespace RatBot.Discord.Tests.Features.RoleColours;

[TestFixture]
public sealed class RoleColourAdminModuleTests
{
    [Test]
    public void AdminCommands()
    {
        // Arrange
        Type moduleType = typeof(RoleColourAdminModule);

        // Act
        GroupAttribute group = moduleType.GetCustomAttribute<GroupAttribute>() ??
                               throw new InvalidOperationException("Expected group attribute.");
        DefaultMemberPermissionsAttribute permissions =
            moduleType.GetCustomAttribute<DefaultMemberPermissionsAttribute>()
            ?? throw new InvalidOperationException("Expected default permissions attribute.");
        string[] commandNames = moduleType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttribute<SlashCommandAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .ToArray();

        // Assert
        group.Name.ShouldBe("colour-admin");
        commandNames.ShouldBe(["upsert", "delete", "list", "sync"], true);
        permissions.Permissions.ShouldBe(GuildPermission.Administrator);
    }
}