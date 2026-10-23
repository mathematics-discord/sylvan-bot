namespace RatBot.Features.Bridge;

public static class BridgeText
{
    /// <summary>Splits at line or word boundaries and closes/reopens triple-backtick code blocks.</summary>
    public static ImmutableArray<string> Split(string text)
    {
        const int limit = 2000;

        if (text.Length <= limit)
            return text.Length == 0 ? [] : [text];

        var chunks = ImmutableArray.CreateBuilder<string>();
        var offset = 0;
        string? fence = null;
        var atLineStart = true;

        while (offset < text.Length)
        {
            var prefix = fence is null ? string.Empty : fence + "\n";
            var available = limit - prefix.Length - 4;
            var length = Math.Min(available, text.Length - offset);

            if (length < text.Length - offset)
            {
                var newline = text.LastIndexOf('\n', offset + length - 1, length);
                var space = text.LastIndexOf(' ', offset + length - 1, length);

                if (newline >= offset + length / 2)
                    length = newline - offset + 1;
                else if (space > offset + length / 2)
                    length = space - offset + 1;

                // Do not split a UTF-16 surrogate pair or a CRLF sequence.
                var last = text[offset + length - 1];

                if (char.IsHighSurrogate(last) || last == '\r')
                    length--;
            }

            var segment = text.Substring(offset, length);
            UpdateFence(segment, ref fence, ref atLineStart);
            var suffix = fence is null ? string.Empty : "\n```";
            var chunk = prefix + segment + suffix;

            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            offset += length;
        }

        return chunks.ToImmutable();
    }

    private static void UpdateFence(string segment, ref string? fence, ref bool atLineStart)
    {
        foreach (var line in segment.Split('\n'))
        {
            var trimmed = line.TrimStart();

            if (atLineStart && trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (fence is not null)
                    fence = null;
                else
                    fence = trimmed.Length <= 64 ? trimmed.TrimEnd('\r') : "```";
            }

            atLineStart = true;
        }

        atLineStart = segment.EndsWith('\n');
    }
}
