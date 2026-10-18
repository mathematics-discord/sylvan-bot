using System.Net;
using Discord;
using NSubstitute;
using RatBot.Features.EmojiYoink;
using Serilog;

namespace RatBot.Tests.Features.EmojiYoink;

public sealed class EmojiYoinkTests
{
    [TestCase("rat", "rat")]
    [TestCase("rat~1", "rat")]
    [TestCase("rat~27", "rat")]
    public void EmojiName_normalises_terminal_disambiguation_suffix(string invokedName, string expected)
    {
        var result = EmojiName.Normalize(invokedName);

        result.ShouldBe(expected);
    }

    [Test]
    public async Task ImportAsync_rejects_invalid_name_before_download_or_creation()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        var importer =
            new DiscordGuildEmojiImporter(client, new LoggerConfiguration().CreateLogger());

        var guild = Substitute.For<IGuild>();
        var moderator = Substitute.For<IUser>();
        var source = Emote.Parse("<:a~1:789>");

        var exception = await Should.ThrowAsync<ArgumentException>(() =>
            importer.ImportAsync(guild, moderator, source, CancellationToken.None));

        exception.ParamName.ShouldBe("invokedSourceName");
        handler.RequestUri.ShouldBeNull();
        await guild.DidNotReceiveWithAnyArgs().CreateEmoteAsync(null!, default);
    }

    [Test]
    public async Task ImportAsync_uses_source_emoji_for_animated_download_and_normalises_name()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        var importer =
            new DiscordGuildEmojiImporter(client, new LoggerConfiguration().CreateLogger());

        var guild = Substitute.For<IGuild>();
        var moderator = Substitute.For<IUser>();
        moderator.Id.Returns(456UL);
        moderator.Username.Returns("moderator");
        var source = Emote.Parse("<a:rat~27:789>");

        var created = (GuildEmote)
            Activator.CreateInstance(
                typeof(GuildEmote),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [987UL, "rat", true, false, true, Array.Empty<ulong>(), null!, true],
                null
            )!;

        string? importedName = null;
        RequestOptions? requestOptions = null;

        guild
            .CreateEmoteAsync(default!, default, default, default)
            .ReturnsForAnyArgs(call =>
            {
                importedName = call.ArgAt<string>(0);
                requestOptions = call.ArgAt<RequestOptions>(3);
                return created;
            });

        var result = await importer.ImportAsync(guild, moderator, source, CancellationToken.None);

        result.ShouldBe(created);
        handler.RequestUri.ShouldBe(new Uri("https://cdn.discordapp.com/emojis/789.webp?animated=true"));
        importedName.ShouldBe("rat");
        requestOptions.ShouldNotBeNull();
        requestOptions.AuditLogReason.ShouldBe("Emoji yoink by moderator 456; source emoji 789");
        await guild.ReceivedWithAnyArgs(1).CreateEmoteAsync(default!, default, default, default);
    }

    [Test]
    public void Message_parser_extracts_one_custom_emoji_from_surrounding_text()
    {
        var source = CustomEmojiMentionParser.ParseMessage("hello 😀,\n  <a:rat:789>! more words");
        source.Id.ShouldBe(789UL);
        source.Animated.ShouldBeTrue();
    }

    [Test]
    public void Message_parser_rejects_multiple_occurrences_of_same_emoji()
    {
        Should.Throw<FormatException>(() =>
            CustomEmojiMentionParser.ParseMessage("<:rat:789> and <:rat:789>"));
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public async Task ImportAsync_CdnFailure_PreservesStatusAndDoesNotUpload(HttpStatusCode statusCode)
    {
        // Arrange
        using var handler = new RecordingHandler(statusCode);
        using var client = new HttpClient(handler);
        var importer = new DiscordGuildEmojiImporter(client, Log.Logger);
        var guild = Substitute.For<IGuild>();

        // Act
        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            importer.ImportAsync(guild, Substitute.For<IUser>(), Emote.Parse("<:rat:789>"), CancellationToken.None));

        // Assert
        exception.StatusCode.ShouldBe(statusCode);
        await guild.DidNotReceiveWithAnyArgs().CreateEmoteAsync(default!, default, default, default);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ImportAsync_OversizedImage_DisposesDownloadAndDoesNotUpload(bool knownLength)
    {
        // Arrange
        var content = new SizedContent(256 * 1024 + 1, knownLength);
        using var handler = new RecordingHandler(HttpStatusCode.OK, content);
        using var client = new HttpClient(handler);
        var importer = new DiscordGuildEmojiImporter(client, Log.Logger);
        var guild = Substitute.For<IGuild>();

        // Act
        await Should.ThrowAsync<InvalidDataException>(() =>
            importer.ImportAsync(guild, Substitute.For<IUser>(), Emote.Parse("<:rat:789>"), CancellationToken.None));

        // Assert
        content.IsDisposed.ShouldBeTrue();
        await guild.DidNotReceiveWithAnyArgs().CreateEmoteAsync(default!, default, default, default);
    }

    [Test]
    public async Task ImportAsync_DownloadTimeout_ThrowsTimeoutWithoutUploading()
    {
        // Arrange
        using var handler = new RecordingHandler(HttpStatusCode.OK,
            failure: new OperationCanceledException("Download timed out"));
        using var client = new HttpClient(handler);
        var importer = new DiscordGuildEmojiImporter(client, Log.Logger);
        var guild = Substitute.For<IGuild>();

        // Act
        await Should.ThrowAsync<TimeoutException>(() =>
            importer.ImportAsync(guild, Substitute.For<IUser>(), Emote.Parse("<:rat:789>"), CancellationToken.None));

        // Assert
        await guild.DidNotReceiveWithAnyArgs().CreateEmoteAsync(default!, default, default, default);
    }

    [Test]
    public async Task ImportAsync_CallerCancels_PropagatesCancellationWithoutUploading()
    {
        // Arrange
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var importer = new DiscordGuildEmojiImporter(client, Log.Logger);
        var guild = Substitute.For<IGuild>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        await Should.ThrowAsync<OperationCanceledException>(() =>
            importer.ImportAsync(guild, Substitute.For<IUser>(), Emote.Parse("<:rat:789>"), cancellation.Token));

        // Assert
        await guild.DidNotReceiveWithAnyArgs().CreateEmoteAsync(default!, default, default, default);
    }

    [Test]
    public async Task ImportAsync_UploadThrows_DisposesImageAndPreservesException()
    {
        // Arrange
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var importer = new DiscordGuildEmojiImporter(client, Log.Logger);
        var guild = Substitute.For<IGuild>();
        var failure = new HttpRequestException("Upload failed");
        Stream? uploadStream = null;
        guild.CreateEmoteAsync(default!, default, default, default)
            .ReturnsForAnyArgs(call =>
            {
                uploadStream = call.ArgAt<Image>(1).Stream;
                return Task.FromException<GuildEmote>(failure);
            });

        // Act
        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            importer.ImportAsync(guild, Substitute.For<IUser>(), Emote.Parse("<:rat:789>"), CancellationToken.None));

        // Assert
        exception.ShouldBeSameAs(failure);
        uploadStream.ShouldNotBeNull();
        uploadStream.CanRead.ShouldBeFalse();
    }

    private sealed class SizedContent(int size, bool knownLength) : HttpContent
    {
        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(new byte[size]).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = size;
            return knownLength;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, HttpContent? content = null, Exception? failure = null)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return failure is not null
                ? Task.FromException<HttpResponseMessage>(failure)
                : Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = content ?? new ByteArrayContent([1, 2, 3]),
                });
        }
    }
}