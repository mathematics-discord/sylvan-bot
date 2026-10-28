using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Features.Logging;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [Group("logs", "Moderation logging commands.")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented")]
    public sealed class LoggingModule(IDbContextFactory<BotDbContext> contextFactory) : InteractionModuleBase<IInteractionContext>
    {
        [SlashCommand("setup", "View or update moderation logging configuration.")]
        public async Task SetupAsync(
            [Summary("enabled", "Whether moderation logging is enabled.")]
            bool? enabled = null,
            [Summary("delete-log-channel", "Channel for delete and bulk-delete logs.")] [ChannelTypes(ChannelType.Text, ChannelType.News)]
            IChannel? deleteLogChannel = null,
            [Summary("edit-log-channel", "Channel for edit logs.")] [ChannelTypes(ChannelType.Text, ChannelType.News)]
            IChannel? editLogChannel = null)
        {
            if (!await ValidateTextContextAsync())
                return;

            await DeferAsync(ephemeral: true);
            await using var dbContext = await contextFactory.CreateDbContextAsync();

            var configuration = await dbContext.LoggingConfigurations
                .SingleOrDefaultAsync(configuration =>
                    configuration.GuildId == Context.Guild.Id);
            var isNew = configuration is null;
            configuration ??= new LoggingConfiguration { GuildId = Context.Guild.Id };
            var hasUpdates = enabled is not null || deleteLogChannel is not null || editLogChannel is not null;

            if (hasUpdates)
            {
                var updatedEnabled = enabled ?? configuration.Enabled;
                var deleteLogChannelId = deleteLogChannel?.Id ?? configuration.DeleteLogChannelId;
                var editLogChannelId = editLogChannel?.Id ?? configuration.EditLogChannelId;

                var enablingWithoutDestination = updatedEnabled && deleteLogChannelId is null && editLogChannelId is null;

                if (enablingWithoutDestination)
                {
                    await ModifyOriginalResponseAsync(response =>
                        response.Content = "Enable logging only after setting a delete or edit log channel.");

                    return;
                }

                configuration.Enabled = updatedEnabled;
                configuration.DeleteLogChannelId = deleteLogChannelId;
                configuration.EditLogChannelId = editLogChannelId;

                if (isNew)
                    dbContext.LoggingConfigurations.Add(configuration);

                await dbContext.SaveChangesAsync();
            }

            await ModifyOriginalResponseAsync(response => response.Content = FormatConfiguration(configuration));
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
            IChannel? channel = null)
        {
            if (!await ValidateTextContextAsync())
                return;

            await DeferAsync(ephemeral: true);
            var targetChannel = channel ?? Context.Channel;
            await using var dbContext = await contextFactory.CreateDbContextAsync();

            var isExcluded = await dbContext.LoggingExcludedChannels
                .AnyAsync(excluded =>
                    excluded.GuildId == Context.Guild.Id && excluded.ChannelId == targetChannel.Id);

            if (isExcluded)
            {
                await ModifyOriginalResponseAsync(response => response.Content = $"<#{targetChannel.Id}> is already excluded from logging.");

                return;
            }

            dbContext.LoggingExcludedChannels.Add(
                new LoggingExcludedChannel
                {
                    GuildId = Context.Guild.Id,
                    ChannelId = targetChannel.Id,
                    ExcludedAtUtc = DateTimeOffset.UtcNow,
                });
            await dbContext.SaveChangesAsync();

            await ModifyOriginalResponseAsync(response => response.Content = $"Logging is now excluded in <#{targetChannel.Id}>.");
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
            IChannel? channel = null)
        {
            if (!await ValidateTextContextAsync())
                return;

            await DeferAsync(ephemeral: true);
            var targetChannel = channel ?? Context.Channel;
            await using var dbContext = await contextFactory.CreateDbContextAsync();

            var exclusion = await dbContext.LoggingExcludedChannels
                .SingleOrDefaultAsync(excluded =>
                    excluded.GuildId == Context.Guild.Id && excluded.ChannelId == targetChannel.Id);

            if (exclusion is null)
            {
                await ModifyOriginalResponseAsync(response => response.Content = $"<#{targetChannel.Id}> was not excluded from logging.");

                return;
            }

            dbContext.LoggingExcludedChannels.Remove(exclusion);
            await dbContext.SaveChangesAsync();

            await ModifyOriginalResponseAsync(response => response.Content = $"Logging is enabled again in <#{targetChannel.Id}>.");
        }

        [SlashCommand("exclusions", "List channels excluded from moderation logging.")]
        public async Task ExclusionsAsync()
        {
            if (!await ValidateTextContextAsync())
                return;

            await DeferAsync(ephemeral: true);
            await using var dbContext = await contextFactory.CreateDbContextAsync();

            var channelIds = await dbContext.LoggingExcludedChannels
                .AsNoTracking()
                .Where(exclusion => exclusion.GuildId == Context.Guild.Id)
                .OrderBy(exclusion => exclusion.ChannelId)
                .Select(exclusion => exclusion.ChannelId)
                .ToArrayAsync();

            if (channelIds.Length == 0)
            {
                await ModifyOriginalResponseAsync(response => response.Content = "No channels are excluded from logging.");

                return;
            }

            const string heading = "Excluded logging channels:";
            List<string> responses = [];
            var content = heading;

            foreach (var channelId in channelIds)
            {
                var line = $"\n- <#{channelId}>";

                if (content.Length + line.Length > 2000)
                {
                    responses.Add(content);
                    content = heading;
                }

                content += line;
            }

            responses.Add(content);
            await ModifyOriginalResponseAsync(response => response.Content = responses[0]);

            IEnumerable<string> followups = responses
                .Skip(1);

            foreach (var followup in followups)
                await FollowupAsync(text: followup, ephemeral: true);
        }

        private static string FormatConfiguration(LoggingConfiguration configuration)
        {
            var deleteLogChannel = configuration.DeleteLogChannelId is null ? "unset" : $"<#{configuration.DeleteLogChannelId.Value}>";

            var editLogChannel = configuration.EditLogChannelId is null ? "unset" : $"<#{configuration.EditLogChannelId.Value}>";

            return "Logging configuration:\n"
                   + $"- Enabled: {configuration.Enabled}\n"
                   + $"- Delete log channel: {deleteLogChannel}\n"
                   + $"- Edit log channel: {editLogChannel}";
        }

        private async Task<bool> ValidateTextContextAsync()
        {
            var isTextContext = Context.Channel is ITextChannel or IThreadChannel;

            if (isTextContext)
                return true;

            await RespondAsync("This command can only be used in a text channel.", ephemeral: true);

            return false;
        }
    }
}