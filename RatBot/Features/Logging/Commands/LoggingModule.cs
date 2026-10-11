using System.Text;
using Microsoft.EntityFrameworkCore;
using RatBot.Domain.Features.Logging;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Features.Logging;

namespace RatBot.Features.Logging.Commands;

[Group("logging", "Moderation logging commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class LoggingModule(IDbContextFactory<BotDbContext> contextFactory)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("config", "View or update moderation logging configuration.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ConfigAsync(
        [Summary("enabled", "Whether moderation logging is enabled.")]
        bool? enabled = null,
        [Summary("delete-log-channel", "Channel for delete and bulk-delete logs.")]
        [ChannelTypes(ChannelType.Text, ChannelType.News)]
        IChannel? deleteLogChannel = null,
        [Summary("edit-log-channel", "Channel for edit logs.")] [ChannelTypes(ChannelType.Text, ChannelType.News)]
        IChannel? editLogChannel = null,
        [Summary("retention-period", "Evidence retention period in seconds.")]
        int? retentionPeriod = null
    )
    {
        if (!await ValidateAsync().ConfigureAwait(false))
            return;

        ModerationLoggingStore store = new ModerationLoggingStore(contextFactory);
        ErrorOr<LoggingConfiguration> result = await store
            .UpdateConfigurationAsync(
                Context.Guild.Id,
                enabled,
                deleteLogChannel?.Id,
                editLogChannel?.Id,
                retentionPeriod is null ? null : TimeSpan.FromSeconds(retentionPeriod.Value),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        if (result.IsError)
        {
            await RespondAsync(result.FirstError.Description, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondAsync(FormatConfiguration(result.Value), ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("exclude", "Exclude a channel from moderation logging.")]
    public async Task ExcludeAsync(
        [Summary("channel", "Channel to exclude.")]
        [ChannelTypes(
            ChannelType.Text,
            ChannelType.News,
            ChannelType.Forum,
            ChannelType.Category,
            ChannelType.NewsThread,
            ChannelType.PublicThread,
            ChannelType.PrivateThread
        )]
        IChannel? channel = null
    )
    {
        if (!await ValidateAsync().ConfigureAwait(false))
            return;

        IChannel targetChannel = channel ?? Context.Channel;
        ModerationLoggingStore store = new ModerationLoggingStore(contextFactory);
        ExcludeChannelResult result = await store
            .ExcludeAsync(Context.Guild.Id, targetChannel.Id, DateTimeOffset.UtcNow, CancellationToken.None)
            .ConfigureAwait(false);

        string response =
            result is ExcludeChannelResult.Excluded
                ? $"Logging is now excluded in {Mention(targetChannel)}."
                : $"{Mention(targetChannel)} is already excluded from logging.";

        await RespondAsync(response, ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("include", "Enable moderation logging again for a channel.")]
    public async Task IncludeAsync(
        [Summary("channel", "Channel to include.")]
        [ChannelTypes(
            ChannelType.Text,
            ChannelType.News,
            ChannelType.Forum,
            ChannelType.Category,
            ChannelType.NewsThread,
            ChannelType.PublicThread,
            ChannelType.PrivateThread
        )]
        IChannel? channel = null
    )
    {
        if (!await ValidateAsync().ConfigureAwait(false))
            return;

        IChannel targetChannel = channel ?? Context.Channel;
        ModerationLoggingStore store = new ModerationLoggingStore(contextFactory);
        IncludeChannelResult result = await store
            .IncludeAsync(Context.Guild.Id, targetChannel.Id, CancellationToken.None).ConfigureAwait(false);

        string response =
            result is IncludeChannelResult.Included
                ? $"Logging is enabled again in {Mention(targetChannel)}."
                : $"{Mention(targetChannel)} was not excluded from logging.";

        await RespondAsync(response, ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("exclusions", "List channels excluded from moderation logging.")]
    public async Task ExclusionsAsync()
    {
        if (!await ValidateAsync().ConfigureAwait(false))
            return;

        BotDbContext db = await contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        LoggingExcludedChannel[] exclusions = await db
            .LoggingExcludedChannels.AsNoTracking()
            .Where(exclusion => exclusion.GuildId == Context.Guild.Id)
            .OrderBy(exclusion => exclusion.ChannelId)
            .ToArrayAsync(CancellationToken.None)
            .ConfigureAwait(false);

        if (exclusions.Length == 0)
        {
            await RespondAsync("No channels are excluded from logging.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        StringBuilder builder = new StringBuilder("Excluded logging channels:");

        foreach (LoggingExcludedChannel exclusion in exclusions)
            builder.AppendLine().Append("- <#").Append(exclusion.ChannelId).Append('>');

        await RespondAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
    }

    private async Task<bool> ValidateAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        if (Context.Channel is not ITextChannel && Context.Channel is not IThreadChannel)
        {
            await RespondAsync("This command can only be used in a text channel.", ephemeral: true)
                .ConfigureAwait(false);
            return false;
        }

        IGuildUser guildUser = (IGuildUser)Context.User;

        if (!guildUser.GuildPermissions.Administrator)
        {
            await RespondAsync("You need Administrator permission to manage logging exclusions.", ephemeral: true)
                .ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private static string FormatConfiguration(LoggingConfiguration configuration)
    {
        string deleteLogChannel = configuration.DeleteLogChannelId is null
            ? "unset"
            : $"<#{configuration.DeleteLogChannelId.Value}>";
        string editLogChannel = configuration.EditLogChannelId is null
            ? "unset"
            : $"<#{configuration.EditLogChannelId.Value}>";

        return "Logging configuration:\n"
               + $"- Enabled: {configuration.Enabled}\n"
               + $"- Delete log channel: {deleteLogChannel}\n"
               + $"- Edit log channel: {editLogChannel}\n"
               + $"- Evidence retention: {(int)configuration.EvidenceRetentionPeriod.TotalSeconds}s";
    }

    private static string Mention(IChannel channel)
    {
        return $"<#{channel.Id}>";
    }
}