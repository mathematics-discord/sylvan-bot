using System.Net;
using RatBot.Features.Logging.Gateway;
using Serilog.Core;
using Shouldly;

namespace RatBot.Discord.Tests.Features.Logging;

[TestFixture]
public sealed class AttachmentEvidenceDownloaderTests
{
    [Test]
    public async Task TryDownloadAsync_WhenBodyIsExactLimit_ReturnsBytes()
    {
        byte[] body = [1, 2, 3, 4];

        byte[]? result = await DownloadAsync(body, body.Length, body.Length);

        result.ShouldBe(body);
    }

    [Test]
    public async Task TryDownloadAsync_WhenDeclaredContentLengthExceedsLimit_ReturnsNull()
    {
        byte[] body = [1, 2, 3, 4];

        byte[]? result = await DownloadAsync(body, body.Length, body.Length - 1);

        result.ShouldBeNull();
    }

    [Test]
    public async Task TryDownloadAsync_WhenContentLengthIsMissing_StillEnforcesActualLimit()
    {
        byte[] body = [1, 2, 3, 4, 5];

        byte[]? result = await DownloadAsync(body, null, 4);

        result.ShouldBeNull();
    }

    [Test]
    public async Task TryDownloadAsync_WhenContentLengthIsMisleading_StillEnforcesActualLimit()
    {
        byte[] body = [1, 2, 3, 4, 5];

        byte[]? result = await DownloadAsync(body, 1, 4);

        result.ShouldBeNull();
    }

    [Test]
    public async Task ReadLimitedAsync_WhenBodyExceedsLimit_ReadsOnlyLimitPlusOneBytes()
    {
        CountingStream stream = new CountingStream([1, 2, 3, 4, 5, 6, 7]);

        byte[]? result = await AttachmentEvidenceDownloader.ReadLimitedAsync(stream, 3, CancellationToken.None);

        result.ShouldBeNull();
        stream.BytesRead.ShouldBe(4);
    }

    private static async Task<byte[]?> DownloadAsync(byte[] body, long? declaredLength, long maxBytes)
    {
        using HttpClient httpClient = new HttpClient(new StaticResponseHandler(body, declaredLength));

        httpClient.BaseAddress = new Uri("https://example.invalid");

        return await AttachmentEvidenceDownloader.TryDownloadAsync(httpClient, "/attachment", maxBytes, Logger.None,
            CancellationToken.None);
    }

    private sealed class StaticResponseHandler(byte[] body, long? declaredLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
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