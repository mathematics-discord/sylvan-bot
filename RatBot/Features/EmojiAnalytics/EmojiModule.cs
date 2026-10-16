using System.Text;
using RatBot.Features.EmojiAnalytics.Persistence;

namespace RatBot.Features.EmojiAnalytics;

[Group("emoji", "Emoji analytics commands.")]
[DefaultMemberPermissions(GuildPermission.SendMessages)]
public sealed class EmojiModule(EmojiUsageStore store, IOptions<EmojiAnalyticsOptions> options)
    : InteractionModuleBase<IInteractionContext>
{
    private const string UsagePageCustomIdPrefix = "emoji-usage";
    private readonly EmojiAnalyticsOptions _options = options.Value;

    private static string BuildUsagePageCustomId(ulong ownerUserId, int page)
    {
        return $"{UsagePageCustomIdPrefix}:page:{ownerUserId}:{page}";
    }

    [SlashCommand("usage", "Show top emojis by usage.")]
    public async Task UsageAsync()
    {
        await RespondWithUsagePageAsync(1).ConfigureAwait(false);
    }

    [ComponentInteraction($"{UsagePageCustomIdPrefix}:page:*:*", true)]
    public async Task UsagePageAsync(ulong ownerUserId, int page)
    {
        if (Context.User.Id != ownerUserId)
        {
            await RespondAsync("This emoji usage panel is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await UpdateUsagePageAsync(page).ConfigureAwait(false);
    }

    private ComponentBuilderV2 BuildUsagePageComponents(EmojiUsagePage page, ulong ownerUserId)
    {
        return new ComponentBuilderV2(
            new ContainerBuilder()
                .WithTextDisplay(
                    new TextDisplayBuilder().WithContent($"## Emoji Usage Counts (Page {page.Page}/{page.TotalPages})"))
                .WithTextDisplay(new TextDisplayBuilder().WithContent(BuildUsagePageText(page.Items))),
            new ActionRowBuilder().WithComponents([
                new ButtonBuilder()
                    .WithStyle(ButtonStyle.Secondary)
                    .WithLabel("Previous")
                    .WithCustomId(BuildUsagePageCustomId(ownerUserId, page.Page - 1))
                    .WithDisabled(page.Page <= 1),
                new ButtonBuilder()
                    .WithStyle(ButtonStyle.Primary)
                    .WithLabel("Next")
                    .WithCustomId(BuildUsagePageCustomId(ownerUserId, page.Page + 1))
                    .WithDisabled(page.Page >= page.TotalPages)
            ])
        );
    }

    private string BuildUsagePageText(ImmutableArray<EmojiUsageEntry> rows)
    {
        StringBuilder text = new StringBuilder();

        foreach (EmojiUsageEntry row in rows)
            text.AppendLine($"{FormatEmojiForDisplay(row.EmojiId)}: {row.ReactionUsageCount + row.MessageUsageCount}");

        return text.ToString();
    }

    private string FormatEmojiForDisplay(ulong emojiId)
    {
        GuildEmote? guildEmote = Context.Guild?.Emotes.FirstOrDefault(x => x.Id == emojiId);

        return guildEmote is not null ? guildEmote.ToString() : $"[custom:{emojiId}]";
    }

    private async Task<ErrorOr<EmojiUsagePage>> GetUsagePageAsync(int page)
    {
        IGuild? guild = await Context.Client.GetGuildAsync(Context.Guild.Id, CacheMode.CacheOnly);
        if (guild is null)
            return Error.Unexpected(description: "Tracked guild emoji are not available yet.");

        return await store.GetUsagePageAsync(guild.Id, guild.Emotes.Select(emote => emote.Id).ToArray(), page);
    }

    private async Task RespondWithUsagePageAsync(int page)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!_options.IsEnabled(Context.Guild.Id))
        {
            await RespondAsync("Emoji analytics are not enabled for this guild.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        ErrorOr<EmojiUsagePage> pageResult = await GetUsagePageAsync(page).ConfigureAwait(false);

        if (pageResult.IsError)
        {
            await RespondAsync(pageResult.FirstError.Description, ephemeral: true).ConfigureAwait(false);
            return;
        }

        ComponentBuilderV2 components = BuildUsagePageComponents(pageResult.Value, Context.User.Id);

        await RespondAsync(ephemeral: true, components: components.Build()).ConfigureAwait(false);
    }

    private async Task UpdateUsagePageAsync(int page)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!_options.IsEnabled(Context.Guild.Id))
        {
            await RespondAsync("Emoji analytics are not enabled for this guild.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        ErrorOr<EmojiUsagePage> pageResult = await GetUsagePageAsync(page).ConfigureAwait(false);

        if (pageResult.IsError)
        {
            await RespondAsync(pageResult.FirstError.Description, ephemeral: true).ConfigureAwait(false);
            return;
        }

        ComponentBuilderV2 components = BuildUsagePageComponents(pageResult.Value, Context.User.Id);

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(message => message.Components = components.Build()).ConfigureAwait(false);
            return;
        }

        await RespondAsync(ephemeral: true, components: components.Build()).ConfigureAwait(false);
    }
}