using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;

namespace RatBot.Application.Features.Logging;

public sealed class MessageEditDiffer
{
    private const double SimilarTokenThreshold = 0.5;

    private readonly IDiffer _differ;

    public MessageEditDiffer()
        : this(Differ.Instance)
    {
    }

    private MessageEditDiffer(IDiffer differ)
    {
        _differ = differ;
    }

    public MessageEditDiff BuildDiff(string before, string after)
    {
        DiffResult tokenDiff = _differ.CreateDiffs(before, after, false, false, MessageTokenChunker.Instance);
        DiffBuilder builder = new DiffBuilder();
        int beforePosition = 0;

        foreach (DiffBlock block in tokenDiff.DiffBlocks)
        {
            AddUnchangedTokens(tokenDiff, builder, beforePosition, block.DeleteStartA);
            beforePosition = block.DeleteStartA;

            AddChangedBlock(tokenDiff, block, builder);

            beforePosition += block.DeleteCountA;
        }

        AddUnchangedTokens(tokenDiff, builder, beforePosition, tokenDiff.PiecesOld.Count);

        return builder.Build();
    }

    private void AddChangedBlock(DiffResult tokenDiff, DiffBlock block, DiffBuilder builder)
    {
        if (block.DeleteCountA == 1 && block.InsertCountB == 1)
        {
            string beforeToken = tokenDiff.PiecesOld[block.DeleteStartA];
            string afterToken = tokenDiff.PiecesNew[block.InsertStartB];

            if (ShouldRefineTokenReplacement(beforeToken, afterToken))
            {
                AddCharacterRefinement(beforeToken, afterToken, builder);
                return;
            }
        }

        AddTokenRange(
            tokenDiff.PiecesOld,
            block.DeleteStartA,
            block.DeleteCountA,
            MessageEditDiffSegmentKind.Removed,
            segment => builder.AddBefore(segment.Text, segment.Kind)
        );
        AddTokenRange(
            tokenDiff.PiecesNew,
            block.InsertStartB,
            block.InsertCountB,
            MessageEditDiffSegmentKind.Added,
            segment => builder.AddAfter(segment.Text, segment.Kind)
        );
    }

    private void AddCharacterRefinement(string beforeToken, string afterToken, DiffBuilder builder)
    {
        DiffResult characterDiff = CreateCharacterDiff(beforeToken, afterToken);
        int beforePosition = 0;

        foreach (DiffBlock block in characterDiff.DiffBlocks)
        {
            AddUnchangedCharacters(characterDiff, builder, beforePosition, block.DeleteStartA);
            beforePosition = block.DeleteStartA;

            AddCharacterRange(
                characterDiff.PiecesOld,
                block.DeleteStartA,
                block.DeleteCountA,
                MessageEditDiffSegmentKind.Removed,
                segment => builder.AddBefore(segment.Text, segment.Kind)
            );
            AddCharacterRange(
                characterDiff.PiecesNew,
                block.InsertStartB,
                block.InsertCountB,
                MessageEditDiffSegmentKind.Added,
                segment => builder.AddAfter(segment.Text, segment.Kind)
            );

            beforePosition += block.DeleteCountA;
        }

        AddUnchangedCharacters(characterDiff, builder, beforePosition, characterDiff.PiecesOld.Count);
    }

    private bool ShouldRefineTokenReplacement(string beforeToken, string afterToken)
    {
        DiffResult characterDiff = CreateCharacterDiff(beforeToken, afterToken);
        int removedCharacters = characterDiff.DiffBlocks.Sum(block => block.DeleteCountA);
        int addedCharacters = characterDiff.DiffBlocks.Sum(block => block.InsertCountB);
        int commonCharacters = Math.Max(beforeToken.Length - removedCharacters, afterToken.Length - addedCharacters);
        int longestTokenLength = Math.Max(beforeToken.Length, afterToken.Length);

        return longestTokenLength > 0 && (double)commonCharacters / longestTokenLength >= SimilarTokenThreshold;
    }

    private DiffResult CreateCharacterDiff(string beforeToken, string afterToken)
    {
        return _differ.CreateDiffs(beforeToken, afterToken, false, false, CharacterChunker.Instance);
    }

    private static void AddUnchangedTokens(DiffResult tokenDiff, DiffBuilder builder, int start, int end)
    {
        AddTokenRange(
            tokenDiff.PiecesOld,
            start,
            end - start,
            MessageEditDiffSegmentKind.Unchanged,
            segment =>
            {
                builder.AddBefore(segment.Text, segment.Kind);
                builder.AddAfter(segment.Text, segment.Kind);
            }
        );
    }

    private static void AddUnchangedCharacters(DiffResult characterDiff, DiffBuilder builder, int start, int end)
    {
        AddCharacterRange(
            characterDiff.PiecesOld,
            start,
            end - start,
            MessageEditDiffSegmentKind.Unchanged,
            segment =>
            {
                builder.AddBefore(segment.Text, segment.Kind);
                builder.AddAfter(segment.Text, segment.Kind);
            }
        );
    }

    private static void AddTokenRange(
        IReadOnlyList<string> tokens,
        int start,
        int count,
        MessageEditDiffSegmentKind kind,
        Action<MessageEditDiffSegment> addSegment
    )
    {
        if (count <= 0)
            return;

        addSegment(new MessageEditDiffSegment(string.Concat(tokens.Skip(start).Take(count)), kind));
    }

    private static void AddCharacterRange(
        IReadOnlyList<string> characters,
        int start,
        int count,
        MessageEditDiffSegmentKind kind,
        Action<MessageEditDiffSegment> addSegment
    )
    {
        if (count <= 0)
            return;

        addSegment(new MessageEditDiffSegment(string.Concat(characters.Skip(start).Take(count)), kind));
    }

    private sealed class DiffBuilder
    {
        private readonly ImmutableArray<MessageEditDiffSegment>.Builder _before =
            ImmutableArray.CreateBuilder<MessageEditDiffSegment>();

        private readonly ImmutableArray<MessageEditDiffSegment>.Builder _after =
            ImmutableArray.CreateBuilder<MessageEditDiffSegment>();

        public void AddBefore(string text, MessageEditDiffSegmentKind kind)
        {
            Add(_before, text, kind);
        }

        public void AddAfter(string text, MessageEditDiffSegmentKind kind)
        {
            Add(_after, text, kind);
        }

        public MessageEditDiff Build()
        {
            return new MessageEditDiff(_before.ToImmutable(), _after.ToImmutable());
        }

        private static void Add(ImmutableArray<MessageEditDiffSegment>.Builder segments, string text,
            MessageEditDiffSegmentKind kind)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (segments.Count > 0 && segments[^1].Kind == kind)
            {
                MessageEditDiffSegment previous = segments[^1];
                segments[^1] = previous with { Text = previous.Text + text };
                return;
            }

            segments.Add(new MessageEditDiffSegment(text, kind));
        }
    }
}