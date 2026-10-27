using SylvanBot.Interactions;

namespace SylvanBot.Features.EmojiYoink;

[DefaultMemberPermissions(GuildPermission.ViewAuditLog)]
[RequireUserPermission(GuildPermission.ViewAuditLog)]
[RequireBotPermission(GuildPermission.CreateGuildExpressions)]
[CommandContextType(InteractionContextType.Guild)]
public sealed class EmojiYoinkModule(DiscordGuildEmojiImporter importer) : SlashCommandBase
{
    [SlashCommand("yoink", "Copy a custom emoji into this server.")]
    public async Task YoinkAsync([Summary("emoji", "The custom emoji to copy.")] string emoji)
    {
        ErrorOr<Emote> sourceResult = CustomEmojiMentionParser.ParseStrict(emoji);

        await ExecuteAsync(sourceResult: sourceResult, rejectLocalSource: false).ConfigureAwait(false);
    }

    [MessageCommand("Yoink emoji")]
    public async Task YoinkMessageAsync(IMessage message)
    {
        ErrorOr<Emote> sourceResult = CustomEmojiMentionParser.ParseMessage(message.Content);

        await ExecuteAsync(sourceResult: sourceResult, rejectLocalSource: true).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(ErrorOr<Emote> sourceResult, bool rejectLocalSource)
    {
        var guild = Context.Guild;

        if (guild is null)
        {
            await RespondAsync(EmojiYoinkErrors.GuildOnly.Description).ConfigureAwait(false);

            return;
        }

        if (sourceResult.IsError)
        {
            await RespondAsync(sourceResult.FirstError.Description).ConfigureAwait(false);

            return;
        }

        var source = sourceResult.Value;

        if (rejectLocalSource && guild.Emotes.Any(emote => emote.Id == source.Id))
        {
            await RespondAsync(EmojiYoinkErrors.SourceAlreadyInGuild.Description).ConfigureAwait(false);

            return;
        }

        await DeferAsync().ConfigureAwait(false);

        ErrorOr<GuildEmote> result = await importer.ImportAsync(guild: guild, moderator: Context.User, source: source, ct: CancellationToken.None)
           .ConfigureAwait(false);

        await result.SwitchFirstAsync(
            async created => await FollowupAsync($"Added emoji {created}.").ConfigureAwait(false),
            async error => await FollowupAsync(error.Description).ConfigureAwait(false)
        );
    }
}
