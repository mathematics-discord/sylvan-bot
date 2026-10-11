using RatBot.Application.Features.Logging;
using Shouldly;

namespace RatBot.Application.Tests.Features.Logging;

[TestFixture]
public sealed class MessageEditDifferTests
{
    [Test]
    public void BuildDiff_WhenSingleTokensAreSimilar_RefinesReplacementToCharacters()
    {
        MessageEditDiff diff = new MessageEditDiffer().BuildDiff("Hayes", "hates");

        diff.Before.Select(segment => new SegmentAssertion(segment.Text, segment.Kind))
            .ShouldBe(
                new[]
                {
                    new SegmentAssertion("H", MessageEditDiffSegmentKind.Removed),
                    new SegmentAssertion("a", MessageEditDiffSegmentKind.Unchanged),
                    new SegmentAssertion("y", MessageEditDiffSegmentKind.Removed),
                    new SegmentAssertion("es", MessageEditDiffSegmentKind.Unchanged)
                }
            );
        diff.After.Select(segment => new SegmentAssertion(segment.Text, segment.Kind))
            .ShouldBe(
                new[]
                {
                    new SegmentAssertion("h", MessageEditDiffSegmentKind.Added),
                    new SegmentAssertion("a", MessageEditDiffSegmentKind.Unchanged),
                    new SegmentAssertion("t", MessageEditDiffSegmentKind.Added),
                    new SegmentAssertion("es", MessageEditDiffSegmentKind.Unchanged)
                }
            );
    }

    [Test]
    public void BuildDiff_WhenReplacementIsLarger_ColoursWholeChangedSpan()
    {
        MessageEditDiff diff = new MessageEditDiffer().BuildDiff(
            "which are `significant' (disregarding the rest",
            "instead (and disregarding the rest"
        );

        diff.Before.Select(segment => new SegmentAssertion(segment.Text, segment.Kind))
            .ShouldBe(
                new[]
                {
                    new SegmentAssertion("which are `significant' (", MessageEditDiffSegmentKind.Removed),
                    new SegmentAssertion("disregarding the rest", MessageEditDiffSegmentKind.Unchanged)
                }
            );
        diff.After.Select(segment => new SegmentAssertion(segment.Text, segment.Kind))
            .ShouldBe(
                new[]
                {
                    new SegmentAssertion("instead (and ", MessageEditDiffSegmentKind.Added),
                    new SegmentAssertion("disregarding the rest", MessageEditDiffSegmentKind.Unchanged)
                }
            );
    }

    private sealed record SegmentAssertion(string Text, MessageEditDiffSegmentKind Kind);
}