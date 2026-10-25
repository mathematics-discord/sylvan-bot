namespace SylvanBot.Tests.Common;

using SylvanBot.Application.Common;

[TestFixture]
public sealed class OptionTests
{
    [Test]
    public void Convert_None_PreservesCase()
    {
        // Arrange
        var none = new None();

        // Act
        Option<string> result = none;

        // Assert
        result.Value.ShouldBeSameAs(none);
    }

    [Test]
    public void Convert_Some_PreservesCaseAndPayload()
    {
        // Arrange
        var payload = new object();
        Some<object> some = new(payload);

        // Act
        Option<object> result = some;

        // Assert
        result.Value.ShouldBeSameAs(some);
        result.Value.ShouldBeOfType<Some<object>>().Value.ShouldBeSameAs(payload);
    }

    [Test]
    public void Convert_SomeWithNullPayload_PreservesSome()
    {
        // Arrange
        Some<string?> some = new(null);

        // Act
        Option<string?> result = some;

        // Assert
        result.Value.ShouldBeOfType<Some<string?>>().Value.ShouldBeNull();
    }

    [Test]
    public void Convert_NullNone_ThrowsArgumentNullException()
    {
        // Arrange
        None none = null!;

        // Act
        Action convert = () => _ = (Option<string>)none;

        // Assert
        Should.Throw<ArgumentNullException>(convert).ParamName.ShouldBe("value");
    }

    [Test]
    public void Convert_NullSome_ThrowsArgumentNullException()
    {
        // Arrange
        Some<string> some = null!;

        // Act
        Action convert = () => _ = (Option<string>)some;

        // Assert
        Should.Throw<ArgumentNullException>(convert).ParamName.ShouldBe("value");
    }
}
