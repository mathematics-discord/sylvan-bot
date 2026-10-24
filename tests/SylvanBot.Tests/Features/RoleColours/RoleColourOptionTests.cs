namespace SylvanBot.Tests.Features.RoleColours;

using SylvanBot.Features.RoleColours;

public sealed class RoleColourOptionTests
{
    [Test]
    public void Update_EqualSourceAndDisplay_ThrowsWithoutChangingMapping()
    {
        // Arrange
        var option = RoleColourOption.Create(1, 10, 20);
        var updatedAt = option.UpdatedAtUtc;

        // Act
        Should.Throw<ArgumentException>(() => option.Update(30, 30));

        // Assert
        option.SourceRoleId.ShouldBe(10UL);
        option.DisplayRoleId.ShouldBe(20UL);
        option.UpdatedAtUtc.ShouldBe(updatedAt);
    }
}