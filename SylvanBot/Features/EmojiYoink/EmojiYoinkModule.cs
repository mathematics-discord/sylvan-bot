using SylvanBot.Interactions;

namespace SylvanBot.Features.EmojiYoink;

[DefaultMemberPermissions(GuildPermission.ViewAuditLog)]
[RequireUserPermission(GuildPermission.ViewAuditLog)]
[RequireBotPermission(GuildPermission.CreateGuildExpressions)]
[CommandContextType(InteractionContextType.Guild)]
public sealed class EmojiYoinkModule(DiscordGuildEmojiImporter importer) : SlashCommandBase
{
    [SlashCommand("yoink", "Copy a custom emoji into this server.")]
    public Task YoinkAsync([Summary("emoji", "The custom emoji to copy.")] string emoji) =>
        ExecuteAsync(emoji, fromMessage: false);

    [MessageCommand("Yoink emoji")]
    public Task YoinkMessageAsync(IMessage message) => ExecuteAsync(message.Content, fromMessage: true);

    private async Task ExecuteAsync(string input, bool fromMessage)
    {
        SocketGuild? guild = Context.Guild;

        try
        {
            if (guild is null)
                throw EmojiYoinkErrors.GuildOnly;

            Emote source = fromMessage ? CustomEmojiMentionParser.ParseMessage(input) : CustomEmojiMentionParser.ParseStrict(input);

            if (fromMessage && guild.Emotes.Any(emote => emote.Id == source.Id))
                throw EmojiYoinkErrors.SourceAlreadyInGuild;

            await DeferAsync().ConfigureAwait(false);
            GuildEmote created = await importer
                .ImportAsync(guild: guild, moderator: Context.User, source: source, ct: CancellationToken.None)
                .ConfigureAwait(false);

            await FollowupAsync($"Added emoji {created}.").ConfigureAwait(false);
        }
        catch (EmojiImportException error)
        {
            if (Context.Interaction.HasResponded)
                await FollowupAsync(error.Message).ConfigureAwait(false);
            else
                await RespondAsync(error.Message).ConfigureAwait(false);
        }
    }
}
