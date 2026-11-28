namespace SylvanBot.Tests.Features.RoleColours;

using SylvanBot.Application.Features.RoleColours;

public sealed class RoleColourOptionTests
{
    [Test]
    public void Update_EqualSourceAndDisplay_ReturnsFailureWithoutChangingMapping()
    {
        // Arrange
        RoleColourOption option = RoleColourOption.Create(1, 10, 20);
        DateTimeOffset updatedAt = option.UpdatedAtUtc;

        // Act
        BotCommandException refusal = Should.Throw<BotCommandException>(() => option.Update(30, 30));

        // Assert
        refusal.Message.ShouldBe("Source and display role IDs must be different.");
        option.SourceRoleId.ShouldBe(10UL);
        option.DisplayRoleId.ShouldBe(20UL);
        option.UpdatedAtUtc.ShouldBe(updatedAt);
    }
}