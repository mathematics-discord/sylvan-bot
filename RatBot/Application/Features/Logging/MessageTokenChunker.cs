using DiffPlex;

namespace RatBot.Application.Features.Logging;

internal sealed class MessageTokenChunker : IChunker
{
    public static readonly MessageTokenChunker Instance = new MessageTokenChunker();

    private MessageTokenChunker()
    {
    }

    public IReadOnlyList<string> Chunk(string text)
    {
        List<string> chunks = new List<string>();
        int position = 0;

        while (position < text.Length)
        {
            int start = position;

            if (char.IsLetterOrDigit(text[position]))
                position = ConsumeWordToken(text, position);
            else
                position = ConsumeNonWordToken(text, position);

            chunks.Add(text[start..position]);
        }

        return chunks;
    }

    private static int ConsumeWordToken(string text, int position)
    {
        while (position < text.Length && char.IsLetterOrDigit(text[position]))
            position++;

        while (position < text.Length && !char.IsLetterOrDigit(text[position]))
            position++;

        return position;
    }

    private static int ConsumeNonWordToken(string text, int position)
    {
        while (position < text.Length && !char.IsLetterOrDigit(text[position]))
            position++;

        return position;
    }
}