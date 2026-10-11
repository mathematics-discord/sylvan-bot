namespace RatBot.Features.Logging.Gateway;

internal static class AttachmentEvidenceDownloader
{
    public static async Task<Option<byte[]>> TryDownloadAsync(
        HttpClient httpClient, string url, long maxBytes, ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new None();

            long? contentLength = response.Content.Headers.ContentLength;

            if (contentLength > maxBytes)
                return new None();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct)
                .ConfigureAwait(false);
            return await ReadLimitedAsync(stream, maxBytes, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Failed to download moderation logging attachment evidence");
            return new None();
        }
    }

    internal static async Task<Option<byte[]>> ReadLimitedAsync(Stream stream, long maxBytes, CancellationToken ct)
    {
        if (maxBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum attachment bytes must not be negative.");

        const int maxReadBufferBytes = 81920;

        long bufferedByteLimit = maxBytes == long.MaxValue ? long.MaxValue : maxBytes + 1;
        long bytesRemaining = bufferedByteLimit;
        int readBufferBytes = (int)Math.Min(maxReadBufferBytes, Math.Max(1, bytesRemaining));
        byte[] readBuffer = new byte[readBufferBytes];

        await using MemoryStream buffer = new MemoryStream(readBufferBytes);

        while (bytesRemaining > 0)
        {
            int bytesToRead = (int)Math.Min(readBuffer.Length, bytesRemaining);
            int bytesRead = await stream.ReadAsync(readBuffer.AsMemory(0, bytesToRead), ct).ConfigureAwait(false);

            if (bytesRead == 0)
                return new Some<byte[]>(buffer.ToArray());

            await buffer.WriteAsync(readBuffer.AsMemory(0, bytesRead), ct);

            bytesRemaining -= bytesRead;
        }

        return buffer.Length > maxBytes ? new None() : new Some<byte[]>(buffer.ToArray());
    }
}