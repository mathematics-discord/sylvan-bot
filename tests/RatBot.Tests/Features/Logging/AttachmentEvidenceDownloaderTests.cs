using System.Net;
using RatBot.Common;
using RatBot.Features.Logging;
using Serilog.Core;
using Shouldly;

namespace RatBot.Tests.Features.Logging;

[TestFixture]
public sealed class AttachmentEvidenceDownloaderTests
{
    [Test]
    public async Task TryDownloadAsync_WhenBodyIsExactLimit_ReturnsBytes()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4];

        // Act
        Option<byte[]> result = await DownloadAsync(body, body.Length, body.Length);

        // Assert
        result.Value.ShouldBeOfType<Some<byte[]>>().Value.ShouldBe(body);
    }

    [Test]
    public async Task TryDownloadAsync_DeclaredContentLengthExceedsLimit_ReturnsNone()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4];

        // Act
        Option<byte[]> result = await DownloadAsync(body, body.Length, body.Length - 1);

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public async Task TryDownloadAsync_WhenContentLengthIsMissing_StillEnforcesActualLimit()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4, 5];

        // Act
        Option<byte[]> result = await DownloadAsync(body, null, 4);

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public async Task TryDownloadAsync_WhenContentLengthIsMisleading_StillEnforcesActualLimit()
    {
        // Arrange
        byte[] body = [1, 2, 3, 4, 5];

        // Act
        Option<byte[]> result = await DownloadAsync(body, 1, 4);

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public async Task ReadLimitedAsync_WhenBodyExceedsLimit_ReadsOnlyLimitPlusOneBytes()
    {
        // Arrange
        await using CountingStream stream = new CountingStream([1, 2, 3, 4, 5, 6, 7]);

        // Act
        Option<byte[]> result = await AttachmentEvidenceDownloader.ReadLimitedAsync(stream, 3, CancellationToken.None);

        // Assert
        result.Value.ShouldBeOfType<None>();
        stream.BytesRead.ShouldBe(4);
    }

    [Test]
    public async Task TryDownloadAsync_EmptyBody_ReturnsSomeEmptyBytes()
    {
        // Arrange
        byte[] body = [];

        // Act
        Option<byte[]> result = await DownloadAsync(body, declaredLength: 0, maxBytes: 0);

        // Assert
        result.Value.ShouldBeOfType<Some<byte[]>>().Value.ShouldBeEmpty();
    }

    [Test]
    public async Task TryDownloadAsync_UnsuccessfulResponse_ReturnsNone()
    {
        // Arrange
        using HttpClient httpClient = new HttpClient(
            new StaticResponseHandler([], declaredLength: 0, status: HttpStatusCode.NotFound));

        // Act
        Option<byte[]> result = await AttachmentEvidenceDownloader.TryDownloadAsync(
            httpClient,
            "https://example.invalid/attachment",
            maxBytes: 4,
            Logger.None,
            CancellationToken.None);

        // Assert
        result.Value.ShouldBeOfType<None>();
    }

    [Test]
    public async Task TryDownloadAsync_CancelledRequest_PropagatesCancellation()
    {
        // Arrange
        using HttpClient httpClient = new HttpClient(new StaticResponseHandler([1], declaredLength: 1));
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            AttachmentEvidenceDownloader.TryDownloadAsync(
                httpClient,
                "https://example.invalid/attachment",
                maxBytes: 4,
                Logger.None,
                cancellation.Token));

        // Assert
        exception.CancellationToken.IsCancellationRequested.ShouldBeTrue();
    }

    private static async Task<Option<byte[]>> DownloadAsync(byte[] body, long? declaredLength, long maxBytes)
    {
        using HttpClient httpClient = new HttpClient(new StaticResponseHandler(body, declaredLength));

        httpClient.BaseAddress = new Uri("https://example.invalid");

        return await AttachmentEvidenceDownloader
            .TryDownloadAsync(
                httpClient: httpClient,
                url: "/attachment",
                maxBytes: maxBytes,
                logger: Logger.None,
                ct: CancellationToken.None)
            .ConfigureAwait(false);
    }

    private sealed class StaticResponseHandler(
        byte[] body,
        long? declaredLength,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new HttpResponseMessage(status)
                { Content = new StaticLengthContent(body, declaredLength) };

            return Task.FromResult(response);
        }
    }

    private sealed class StaticLengthContent(byte[] body, long? declaredLength) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(body, 0, body.Length);
        }

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

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new MemoryStream(body));
        }
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
            int bytesRead = Read(buffer.AsSpan(offset, count));
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            int bytesToRead = Math.Min(buffer.Length, body.Length - _position);

            if (bytesToRead == 0)
                return 0;

            body.AsSpan(_position, bytesToRead).CopyTo(buffer);
            _position += bytesToRead;
            BytesRead += bytesToRead;
            return bytesToRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

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
