using System.Net;
using Discord;
using ErrorOr;
using NSubstitute;
using RatBot.Features.EmojiYoink;
using Serilog;
using Shouldly;

namespace RatBot.Application.Tests.Features.EmojiYoink;

public sealed class EmojiYoinkTests
{
    [TestCase("rat", "rat")]
    [TestCase("rat~1", "rat")]
    [TestCase("rat~27", "rat")]
    public void EmojiName_normalises_terminal_disambiguation_suffix(string invokedName, string expected)
    {
        ErrorOr<string> result = EmojiName.Normalize(invokedName);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(expected);
    }

    [Test]
    public async Task ImportAsync_rejects_invalid_name_before_download_or_creation()
    {
        using RecordingHandler handler = new RecordingHandler(HttpStatusCode.OK);
        using HttpClient client = new HttpClient(handler);

        DiscordGuildEmojiImporter importer =
            new DiscordGuildEmojiImporter(client, new LoggerConfiguration().CreateLogger());

        IGuild guild = Substitute.For<IGuild>();
        IUser moderator = Substitute.For<IUser>();
        Emote source = Emote.Parse("<:a~1:789>");

        ErrorOr<GuildEmote> result = await importer.ImportAsync(guild, moderator, source, CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.ShouldBe(EmojiYoinkErrors.InvalidDestinationName);
        handler.RequestUri.ShouldBeNull();
        await guild.DidNotReceiveWithAnyArgs().CreateEmoteAsync(null!, default);
    }

    [Test]
    public async Task ImportAsync_uses_source_emoji_for_animated_download_and_normalises_name()
    {
        using RecordingHandler handler = new RecordingHandler(HttpStatusCode.OK);
        using HttpClient client = new HttpClient(handler);

        DiscordGuildEmojiImporter importer =
            new DiscordGuildEmojiImporter(client, new LoggerConfiguration().CreateLogger());

        IGuild guild = Substitute.For<IGuild>();
        IUser moderator = Substitute.For<IUser>();
        moderator.Id.Returns(456UL);
        moderator.Username.Returns("moderator");
        Emote source = Emote.Parse("<a:rat~27:789>");

        GuildEmote created = (GuildEmote)
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

        ErrorOr<GuildEmote> result = await importer.ImportAsync(guild, moderator, source, CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(created);
        handler.RequestUri.ShouldBe(new Uri("https://cdn.discordapp.com/emojis/789.webp?animated=true"));
        importedName.ShouldBe("rat");
        requestOptions.ShouldNotBeNull();
        requestOptions.AuditLogReason.ShouldBe("Emoji yoink by moderator 456; source emoji 789");
        await guild.ReceivedWithAnyArgs(1).CreateEmoteAsync(default!, default, default, default);
    }

    [Test]
    public void Message_parser_extracts_one_custom_emoji_from_surrounding_text()
    {
        ErrorOr<Emote> result = CustomEmojiMentionParser.ParseMessage("hello 😀,\n  <a:rat:789>! more words");

        result.IsError.ShouldBeFalse();
        result.Value.Id.ShouldBe(789UL);
        result.Value.Animated.ShouldBeTrue();
    }

    [Test]
    public void Message_parser_rejects_multiple_occurrences_of_same_emoji()
    {
        ErrorOr<Emote> result = CustomEmojiMentionParser.ParseMessage("<:rat:789> and <:rat:789>");

        result.IsError.ShouldBeTrue();
        result.FirstError.ShouldBe(EmojiYoinkErrors.InvalidMessageEmoji);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }
}