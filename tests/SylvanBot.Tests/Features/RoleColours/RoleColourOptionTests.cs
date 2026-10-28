namespace SylvanBot.Tests.Features.RoleColours;

using SylvanBot.Application.Features.RoleColours;

public sealed class RoleColourOptionTests
{
    [Test]
    public void Update_EqualSourceAndDisplay_ReturnsFailureWithoutChangingMapping()
    {
        // Arrange
        var option = RoleColourOption.Create(1, 10, 20).Value;
        var updatedAt = option.UpdatedAtUtc;

        // Act
        var result = option.Update(30, 30);

        // Assert
        result.IsSuccessful.ShouldBeFalse();
        result.Error.ShouldBeOfType<OperationFailureException>().Category.ShouldBe(OperationFailureCategory.Validation);
        option.SourceRoleId.ShouldBe(10UL);
        option.DisplayRoleId.ShouldBe(20UL);
        option.UpdatedAtUtc.ShouldBe(updatedAt);
    }
}