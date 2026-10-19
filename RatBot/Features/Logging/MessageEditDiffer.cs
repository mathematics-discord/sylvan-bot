using System.Text;
using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;

namespace RatBot.Features.Logging;

internal static class MessageEditDiffer
{
    private const double SimilarTokenThreshold = 0.5;
    private const string Red = "\e[1;31m";
    private const string Green = "\e[1;32m";
    private const string Reset = "\e[0m";
    private static readonly CustomFunctionChunker TokenChunker = new(ChunkTokens);

    public static (string Before, string After) BuildDiff(string before, string after)
    {
        var diff = Differ.Instance.CreateDiffs(
            before, after, ignoreWhiteSpace: false, ignoreCase: false, chunker: TokenChunker);

        var beforeBuilder = new StringBuilder();
        var afterBuilder = new StringBuilder();
        AppendDiff(diff, beforeBuilder, afterBuilder, refineTokens: true);
        return (beforeBuilder.ToString(), afterBuilder.ToString());
    }

    private static void AppendDiff(
        DiffResult diff, StringBuilder before, StringBuilder after, bool refineTokens)
    {
        var beforePosition = 0;

        foreach (var block in diff.DiffBlocks)
        {
            AppendUnchanged(diff.PiecesOld, beforePosition, block.DeleteStartA, before, after);
            var characterDiff = refineTokens ? GetCharacterRefinement(diff, block) : null;

            if (characterDiff is null)
            {
                AppendChanged(before, diff.PiecesOld, block.DeleteStartA, block.DeleteCountA, Red);
                AppendChanged(after, diff.PiecesNew, block.InsertStartB, block.InsertCountB, Green);
            }
            else
            {
                AppendDiff(characterDiff, before, after, refineTokens: false);
            }

            beforePosition = block.DeleteStartA + block.DeleteCountA;
        }

        AppendUnchanged(diff.PiecesOld, beforePosition, diff.PiecesOld.Count, before, after);
    }

    private static DiffResult? GetCharacterRefinement(DiffResult tokenDiff, DiffBlock block)
    {
        var isSingleTokenReplacement = block.DeleteCountA == 1 && block.InsertCountB == 1;

        if (!isSingleTokenReplacement)
            return null;

        var beforeToken = tokenDiff.PiecesOld[block.DeleteStartA];
        var afterToken = tokenDiff.PiecesNew[block.InsertStartB];
        var characterDiff = Differ.Instance.CreateDiffs(
            beforeToken, afterToken, ignoreWhiteSpace: false, ignoreCase: false, chunker: CharacterChunker.Instance);

        var removedCharacters = characterDiff.DiffBlocks
            .Sum(change => change.DeleteCountA);

        var addedCharacters = characterDiff.DiffBlocks
            .Sum(change => change.InsertCountB);

        var commonCharacters = Math.Max(beforeToken.Length - removedCharacters, afterToken.Length - addedCharacters);
        var longestTokenLength = Math.Max(beforeToken.Length, afterToken.Length);
        var tokensAreSimilar = longestTokenLength > 0
            && (double)commonCharacters / longestTokenLength >= SimilarTokenThreshold;

        return tokensAreSimilar ? characterDiff : null;
    }

    private static void AppendUnchanged(
        IReadOnlyList<string> pieces, int start, int end, StringBuilder before, StringBuilder after)
    {
        for (var index = start; index < end; index++)
        {
            before.Append(pieces[index]);
            after.Append(pieces[index]);
        }
    }

    private static void AppendChanged(
        StringBuilder output, IReadOnlyList<string> pieces, int start, int count, string color)
    {
        if (count == 0)
            return;

        output.Append(color);

        for (var index = start; index < start + count; index++)
            output.Append(pieces[index]);

        output.Append(Reset);
    }

    private static IReadOnlyList<string> ChunkTokens(string text)
    {
        var chunks = new List<string>();
        var position = 0;

        while (position < text.Length)
        {
            var start = position;

            while (position < text.Length && char.IsLetterOrDigit(text[position]))
                position++;

            while (position < text.Length && !char.IsLetterOrDigit(text[position]))
                position++;

            chunks.Add(text[start..position]);
        }

        return chunks;
    }
}
