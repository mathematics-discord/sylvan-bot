namespace RatBot.Common;

public static class MessageChunker
{
    private const int DefaultMessageCharacterLimit = 2000;

    public static string[] SplitMessageIntoChunks(string message, int chunkSize = DefaultMessageCharacterLimit)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be greater than zero.");

        Queue<string> chunks = new((message.Length / chunkSize) + 1);

        var index = 0;

        while (index < message.Length)
        {
            var remainingLength = message.Length - index;

            if (remainingLength <= chunkSize)
            {
                chunks.Enqueue(message[index..]);
                break;
            }

            var window = message.Substring(index, chunkSize);
            var splitAt = window.LastIndexOf('\n');

            var chunkLength = splitAt > 0 ? splitAt + 1 : chunkSize;

            chunks.Enqueue(message.Substring(index, chunkLength));
            index += chunkLength;
        }

        return chunks.ToArray();
    }
}