using System.Text;
using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;

namespace SylvanBot.Features.Logging;

internal static class MessageEditDiffer
{
    private const double SimilarTokenThreshold = 0.5;
    private const string Red = "\e[1;31m";
    private const string Green = "\e[1;32m";
    private const string Reset = "\e[0m";

    private static readonly CustomFunctionChunker TokenChunker = new CustomFunctionChunker(s => ChunkTokens(s).ToList());

    public static (string Before, string After) BuildDiff(string before, string after)
    {
        var diff = Differ.Instance.CreateDiffs(oldText: before, newText: after, ignoreWhiteSpace: false, ignoreCase: false, chunker: TokenChunker);

        var beforeBuilder = new StringBuilder();
        var afterBuilder = new StringBuilder();
        AppendDiff(diff: diff, before: beforeBuilder, after: afterBuilder, refineTokens: true);

        return (beforeBuilder.ToString(), afterBuilder.ToString());
    }

    private static void AppendDiff(DiffResult diff, StringBuilder before, StringBuilder after, bool refineTokens)
    {
        var beforePosition = 0;

        foreach (var block in diff.DiffBlocks)
        {
            AppendUnchanged(pieces: diff.PiecesOld, start: beforePosition, end: block.DeleteStartA, before: before, after: after);
            var characterDiff = refineTokens ? GetCharacterRefinement(tokenDiff: diff, block: block) : null;

            if (characterDiff is null)
            {
                AppendChanged(output: before, pieces: diff.PiecesOld, start: block.DeleteStartA, count: block.DeleteCountA, color: Red);
                AppendChanged(output: after, pieces: diff.PiecesNew, start: block.InsertStartB, count: block.InsertCountB, color: Green);
            }
            else
                AppendDiff(diff: characterDiff, before: before, after: after, refineTokens: false);

            beforePosition = block.DeleteStartA + block.DeleteCountA;
        }

        AppendUnchanged(pieces: diff.PiecesOld, start: beforePosition, end: diff.PiecesOld.Count, before: before, after: after);
    }

    private static DiffResult? GetCharacterRefinement(DiffResult tokenDiff, DiffBlock block)
    {
        var isSingleTokenReplacement = block is { DeleteCountA: 1, InsertCountB: 1 };

        if (!isSingleTokenReplacement)
            return null;

        var beforeToken = tokenDiff.PiecesOld[block.DeleteStartA];
        var afterToken = tokenDiff.PiecesNew[block.InsertStartB];

        var characterDiff = Differ.Instance.CreateDiffs(
            oldText: beforeToken,
            newText: afterToken,
            ignoreWhiteSpace: false,
            ignoreCase: false,
            chunker: CharacterChunker.Instance
        );

        var removedCharacters = characterDiff.DiffBlocks.Sum(change => change.DeleteCountA);

        var addedCharacters = characterDiff.DiffBlocks.Sum(change => change.InsertCountB);

        var commonCharacters = Math.Max(beforeToken.Length - removedCharacters, afterToken.Length - addedCharacters);
        var longestTokenLength = Math.Max(val1: beforeToken.Length, val2: afterToken.Length);

        var tokensAreSimilar = longestTokenLength > 0 && (double)commonCharacters / longestTokenLength >= SimilarTokenThreshold;

        return tokensAreSimilar ? characterDiff : null;
    }

    private static void AppendUnchanged(IReadOnlyList<string> pieces, int start, int end, StringBuilder before, StringBuilder after)
    {
        for (var index = start; index < end; index++)
        {
            before.Append(pieces[index]);
            after.Append(pieces[index]);
        }
    }

    private static void AppendChanged(StringBuilder output, IReadOnlyList<string> pieces, int start, int count, string color)
    {
        if (count == 0)
            return;

        output.Append(color);

        for (var index = start; index < start + count; index++)
            output.Append(pieces[index]);

        output.Append(Reset);
    }

    private static Queue<string> ChunkTokens(string text)
    {
        Queue<string> chunks = [];
        var position = 0;

        while (position < text.Length)
        {
            var start = position;

            while (position < text.Length && char.IsLetterOrDigit(text[position]))
                position++;

            while (position < text.Length && !char.IsLetterOrDigit(text[position]))
                position++;

            chunks.Enqueue(text[start..position]);
        }

        return chunks;
    }
}