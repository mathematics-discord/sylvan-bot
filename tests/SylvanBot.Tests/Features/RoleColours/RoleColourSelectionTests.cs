using SylvanBot.Application.Features.RoleColours;

namespace SylvanBot.Tests.Features.RoleColours;

public sealed class RoleColourSelectionTests
{
    [Test]
    public void Removals_ValidSoleColour_PreservesSelection()
    {
        // Arrange
        HashSet<ulong> worn = new HashSet<ulong> { 10 };
        HashSet<ulong> eligible = new HashSet<ulong> { 10, 20 };

        // Act
        IReadOnlySet<ulong> removals = RoleColourSelection.Removals(worn, holdsGrant: true, eligible, new HashSet<ulong>());

        // Assert
        removals.ShouldBeEmpty();
    }

    [Test]
    public void Removals_MultipleColoursWithGrant_ClearsAmbiguousSelection()
    {
        // Arrange
        HashSet<ulong> worn = new HashSet<ulong> { 10, 20 };

        // Act
        IReadOnlySet<ulong> removals = RoleColourSelection.Removals(worn, holdsGrant: true, worn, new HashSet<ulong>());

        // Assert
        removals.ShouldBe(worn, ignoreOrder: true);
    }

    [Test]
    public void Removals_LostLastGrant_RemovesOnlyPreviouslyGrantedColours()
    {
        // Arrange
        HashSet<ulong> worn = new HashSet<ulong> { 10, 20 };
        HashSet<ulong> previous = new HashSet<ulong> { 10 };

        // Act
        IReadOnlySet<ulong> removals = RoleColourSelection.Removals(worn, holdsGrant: false, new HashSet<ulong>(), previous);

        // Assert
        removals.ShouldBe(new ulong[] { 10 });
    }
}