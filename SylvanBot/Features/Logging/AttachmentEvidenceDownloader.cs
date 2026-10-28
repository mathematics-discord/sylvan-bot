namespace SylvanBot.Features.Logging;

/// <summary>
///     Downloads attachment evidence while enforcing a maximum response size.
/// </summary>
internal static class AttachmentEvidenceDownloader
{
    /// <summary>
    ///     Downloads a successful response body if it does not exceed <paramref name="maxBytes"/>.
    ///     Returns none for unsuccessful responses, oversized bodies, or download failures.
    /// </summary>
    /// <param name="httpClient">The client used to request the attachment.</param>
    /// <param name="url">The attachment URL.</param>
    /// <param name="maxBytes">The maximum number of response bytes to return.</param>
    /// <param name="logger">The logger used to record download failures.</param>
    /// <param name="ct">A token that cancels the request and response read.</param>
    /// <returns>The downloaded bytes, or none if the response cannot be used.</returns>
    public static async Task<DotNext.Optional<byte[]>> TryDownloadAsync(
        HttpClient httpClient,
        string url,
        long maxBytes,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                requestUri: url,
                completionOption: HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: ct
            );

            if (!response.IsSuccessStatusCode)
                return default;

            var contentLength = response.Content.Headers.ContentLength;

            if (contentLength > maxBytes)
                return default;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);

            return await ReadLimitedAsync(stream: stream, maxBytes: maxBytes, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Debug("Failed to download moderation logging attachment evidence: {ExceptionType}", propertyValue: ex.GetType().Name);

            return default;
        }
    }

    /// <summary>
    ///     Reads a stream up to the limit, returning none if it contains more than <paramref name="maxBytes"/>.
    /// </summary>
    /// <param name="stream">The response stream to read.</param>
    /// <param name="maxBytes">The maximum number of bytes to return.</param>
    /// <param name="ct">A token that cancels the read.</param>
    /// <returns>The bytes read, or none if the stream exceeds the limit.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is negative.</exception>
    internal static async Task<DotNext.Optional<byte[]>> ReadLimitedAsync(Stream stream, long maxBytes, CancellationToken ct)
    {
        if (maxBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum attachment bytes must not be negative.");

        const int maxReadBufferBytes = 81920;

        var bufferedByteLimit = maxBytes == long.MaxValue ? long.MaxValue : maxBytes + 1;
        var bytesRemaining = bufferedByteLimit;
        var readBufferBytes = (int)Math.Min(val1: maxReadBufferBytes, Math.Max(val1: 1, val2: bytesRemaining));
        var readBuffer = new byte[readBufferBytes];

        await using var buffer = new MemoryStream(readBufferBytes);

        while (bytesRemaining > 0)
        {
            var bytesToRead = (int)Math.Min(val1: readBuffer.Length, val2: bytesRemaining);
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(start: 0, length: bytesToRead), cancellationToken: ct);

            if (bytesRead == 0)
                return new DotNext.Optional<byte[]>(buffer.ToArray());

            await buffer.WriteAsync(readBuffer.AsMemory(start: 0, length: bytesRead), cancellationToken: ct);

            bytesRemaining -= bytesRead;
        }

        return buffer.Length > maxBytes ? default : new DotNext.Optional<byte[]>(buffer.ToArray());
    }
}