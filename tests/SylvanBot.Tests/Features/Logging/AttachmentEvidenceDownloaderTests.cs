using System.Net;
using Serilog.Core;
using SylvanBot.Features.Logging;

namespace SylvanBot.Tests.Features.Logging;

[TestFixture]
public sealed class AttachmentEvidenceDownloaderTests
{
    [Test]
    public async Task TryDownloadAsync_WhenBodyIsExactLimit_ReturnsBytes()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4];

        // Act
        Optional<byte[]> result = await DownloadAsync(body: body, declaredLength: body.Length, maxBytes: body.Length);

        // Assert
        result.Value.ShouldBe(body);
    }

    [Test]
    public async Task TryDownloadAsync_DeclaredContentLengthExceedsLimit_ReturnsNone()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4];

        // Act
        Optional<byte[]> result = await DownloadAsync(body: body, declaredLength: body.Length, body.Length - 1);

        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public async Task TryDownloadAsync_WhenContentLengthIsMissing_StillEnforcesActualLimit()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4, 5];

        // Act
        Optional<byte[]> result = await DownloadAsync(body: body, declaredLength: null, maxBytes: 4);

        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public async Task TryDownloadAsync_WhenContentLengthIsMisleading_StillEnforcesActualLimit()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4, 5];

        // Act
        Optional<byte[]> result = await DownloadAsync(body: body, declaredLength: 1, maxBytes: 4);

        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public async Task ReadLimitedAsync_WhenBodyExceedsLimit_ReadsOnlyLimitPlusOneBytes()
    {
        // Arrange
        await using var stream = new CountingStream([1, 2, 3, 4, 5, 6, 7]);

        // Act
        Optional<byte[]> result = await AttachmentEvidenceDownloader.ReadLimitedAsync(stream: stream, maxBytes: 3, ct: CancellationToken.None);

        // Assert
        result.IsUndefined.ShouldBeTrue();
        stream.BytesRead.ShouldBe(4);
    }

    [Test]
    public async Task TryDownloadAsync_EmptyBody_ReturnsSomeEmptyBytes()
    {
        // Arrange
        byte[] body = [];

        // Act
        Optional<byte[]> result = await DownloadAsync(body: body, declaredLength: 0, maxBytes: 0);

        // Assert
        result.Value.ShouldBeEmpty();
    }

    [Test]
    public async Task TryDownloadAsync_UnsuccessfulResponse_ReturnsNone()
    {
        // Arrange
        using var httpClient = new HttpClient(new StaticResponseHandler([], declaredLength: 0, status: HttpStatusCode.NotFound));

        // Act
        Optional<byte[]> result = await AttachmentEvidenceDownloader.TryDownloadAsync(
            httpClient: httpClient,
            "https://example.invalid/attachment",
            maxBytes: 4,
            logger: Logger.None,
            ct: CancellationToken.None
        );

        // Assert
        result.IsUndefined.ShouldBeTrue();
    }

    [Test]
    public async Task TryDownloadAsync_CancelledRequest_PropagatesCancellation()
    {
        // Arrange
        using var httpClient = new HttpClient(new StaticResponseHandler([1], declaredLength: 1));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
                                                                                AttachmentEvidenceDownloader.TryDownloadAsync(
                                                                                    httpClient: httpClient,
                                                                                    "https://example.invalid/attachment",
                                                                                    maxBytes: 4,
                                                                                    logger: Logger.None,
                                                                                    ct: cancellation.Token
                                                                                )
        );

        // Assert
        exception.CancellationToken.IsCancellationRequested.ShouldBeTrue();
    }

    private static async Task<Optional<byte[]>> DownloadAsync(byte[] body, long? declaredLength, long maxBytes)
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(body: body, declaredLength: declaredLength));

        httpClient.BaseAddress = new Uri("https://example.invalid");

        return await AttachmentEvidenceDownloader
           .TryDownloadAsync(httpClient: httpClient, "/attachment", maxBytes: maxBytes, logger: Logger.None, ct: CancellationToken.None)
           .ConfigureAwait(false);
    }

    private sealed class StaticResponseHandler(byte[] body, long? declaredLength, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status) { Content = new StaticLengthContent(body: body, declaredLength: declaredLength) };

            return Task.FromResult(response);
        }
    }

    private sealed class StaticLengthContent(byte[] body, long? declaredLength) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(buffer: body, offset: 0, count: body.Length);

        protected override bool TryComputeLength(out long length)
        {
            if (declaredLength is null)
            {
                length = 0;

                return false;
            }

            length = declaredLength.Value;

            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(body));
    }

    private sealed class CountingStream(byte[] body) : Stream
    {
        private int _position;

        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => body.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = Read(buffer.AsSpan(start: offset, length: count));

            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesToRead = Math.Min(val1: buffer.Length, body.Length - _position);

            if (bytesToRead == 0)
                return 0;

            body.AsSpan(start: _position, length: bytesToRead).CopyTo(buffer);
            _position += bytesToRead;
            BytesRead += bytesToRead;

            return bytesToRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}