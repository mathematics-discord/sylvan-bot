namespace SylvanBot.Features.EmojiYoink;

using System.Net;
using Discord.Net;
using SylvanBot.Interactions;

[DefaultMemberPermissions(GuildPermission.ManageEmojisAndStickers)]
[RequireUserPermission(GuildPermission.ManageEmojisAndStickers)]
[RequireBotPermission(GuildPermission.CreateGuildExpressions)]
[CommandContextType(InteractionContextType.Guild)]
public sealed class EmojiYoinkModule(DiscordGuildEmojiImporter importer, ILogger logger) : SlashCommandBase
{
    [SlashCommand("yoink", "Copy a custom emoji into this server.")]
    public async Task YoinkAsync([Summary("emoji", "The custom emoji to copy.")] string emoji)
    {
        await ExecuteAsync(emoji, rejectLocalSource: false).ConfigureAwait(false);
    }

    [MessageCommand("Yoink emoji")]
    public async Task YoinkMessageAsync(IMessage message)
    {
        await ExecuteAsync(message.Content, rejectLocalSource: true).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(string input, bool rejectLocalSource)
    {
        var guild = Context.Guild;

        if (guild is null)
        {
            await RespondAsync(EmojiYoinkMessages.GuildOnly).ConfigureAwait(false);
            return;
        }

        Emote source;
        try
        {
            source = rejectLocalSource
                ? CustomEmojiMentionParser.ParseMessage(input)
                : CustomEmojiMentionParser.ParseStrict(input);
        }
        catch (FormatException)
        {
            var message = rejectLocalSource
                ? EmojiYoinkMessages.InvalidMessageEmoji
                : EmojiYoinkMessages.InvalidCustomEmoji;

            await RespondAsync(message).ConfigureAwait(false);
            return;
        }

        if (rejectLocalSource && guild.Emotes.Any(emote => emote.Id == source.Id))
        {
            await RespondAsync(EmojiYoinkMessages.SourceAlreadyInGuild).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        string response;
        try
        {
            var created = await importer.ImportAsync(guild, Context.User, source, CancellationToken.None)
                .ConfigureAwait(false);

            response = $"Added emoji {created}.";
        }
        catch (ArgumentException ex) when (ex.ParamName == "invokedSourceName")
        {
            response = EmojiYoinkMessages.InvalidDestinationName;
        }
        catch (InvalidDataException)
        {
            response = EmojiYoinkMessages.ImageTooLarge;
        }
        catch (HttpRequestException ex)
        {
            logger.Warning(ex, "Emoji download failed in guild {GuildId}, source {SourceEmojiId}", guild.Id, source.Id);
            response = ex.StatusCode == HttpStatusCode.NotFound
                ? EmojiYoinkMessages.SourceUnavailable
                : EmojiYoinkMessages.DownloadFailed;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            logger.Warning(ex, "Emoji download failed in guild {GuildId}, source {SourceEmojiId}", guild.Id, source.Id);
            response = EmojiYoinkMessages.DownloadFailed;
        }
        catch (HttpException ex)
        {
            logger.Warning(ex, "Emoji upload failed in guild {GuildId} for moderator {ModeratorId}, source {SourceEmojiId}",
                guild.Id, Context.User.Id, source.Id);

            int? code = ex.DiscordCode is null ? null : (int)ex.DiscordCode;
            if (code == 30008)
            {
                response = EmojiYoinkMessages.NoEmojiSlots;
            }
            else if (code == 50035 || ex.HttpCode == HttpStatusCode.BadRequest)
            {
                response = EmojiYoinkMessages.InvalidUpload;
            }
            else if (code == 50013 || ex.HttpCode == HttpStatusCode.Forbidden)
            {
                response = EmojiYoinkMessages.BotMissingPermission;
            }
            else
            {
                response = EmojiYoinkMessages.ImportFailed;
            }
        }

        await FollowupAsync(response).ConfigureAwait(false);
    }
}