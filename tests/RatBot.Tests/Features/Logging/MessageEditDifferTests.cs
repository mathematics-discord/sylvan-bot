namespace RatBot.Tests.Features.Logging;

using RatBot.Features.Logging;

[TestFixture]
public sealed class MessageEditDifferTests
{
    [Test]
    public void BuildDiff_SimilarSingleTokens_RefinesReplacementToCharacters()
    {
        // Arrange
        const string before = "Hayes";
        const string after = "hates";

        // Act
        var diff = MessageEditDiffer.BuildDiff(before, after);

        // Assert
        diff.Before.ShouldBe("\e[1;31mH\e[0ma\e[1;31my\e[0mes");
        diff.After.ShouldBe("\e[1;32mh\e[0ma\e[1;32mt\e[0mes");
    }

    [Test]
    public void BuildDiff_LargerReplacement_ColoursWholeChangedSpan()
    {
        // Arrange
        const string before = "which are `significant' (disregarding the rest";
        const string after = "instead (and disregarding the rest";

        // Act
        var diff = MessageEditDiffer.BuildDiff(before, after);

        // Assert
        diff.Before.ShouldBe("\e[1;31mwhich are `significant' (\e[0mdisregarding the rest");
        diff.After.ShouldBe("\e[1;32minstead (and \e[0mdisregarding the rest");
    }
}
