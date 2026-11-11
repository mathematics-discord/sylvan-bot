using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Activity;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.Activity;
using SylvanBot.Features.RoleReactions;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [Group("activity", "Configure activity XP, role rewards, and channel weights.")]
    [RequireContext(ContextType.Guild)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public sealed class ActivityConfigModule(ActivityService activity, IHostApplicationLifetime lifetime) : SlashCommandBase
    {
        [SlashCommand("weight", "Set a channel or category XP weight, or restore its default.")]
        public async Task WeightAsync(
            [Summary("channel", "Channel or category to configure; threads inherit their parent channel.")]
            IChannel channel,
            [Summary("weight", "XP multiplier from 0.0 to 100.0. Omit to restore inheritance.")]
            [MinValue(0), MaxValue(100)] double? weight = null)
        {
            RequireAdministrator();
            if (channel is not SocketGuildChannel target || target.Guild.Id != Context.Guild.Id || target is SocketThreadChannel)
                throw new BotCommandException("Choose a channel or category from this server, not a thread.");

            if (weight.HasValue && (!double.IsFinite(weight.Value) || weight.Value is < 0 or > 100))
                throw new BotCommandException("Weight must be between 0 and 100.");

            await SaveAsync(async db =>
            {
                var row = await db.ActivityWeights.FindAsync([Context.Guild.Id, channel.Id], lifetime.ApplicationStopping);
                if (!weight.HasValue)
                {
                    if (row is not null)
                        db.ActivityWeights.Remove(row);
                    return;
                }

                if (row is null)
                {
                    row = new ActivityWeight { GuildId = Context.Guild.Id, ChannelId = channel.Id };
                    db.ActivityWeights.Add(row);
                }

                row.Weight = (decimal)weight.Value;
            });
        }

        [SlashCommand("scores", "Set activity roles and XP thresholds; omitted values keep their current settings.")]
        public async Task ScoresAsync(
            [Summary("role_1", "Higher activity role. Replaces role_2 when its threshold is reached.")] IRole? role1 = null,
            [Summary("role_2", "Lower activity role. Replaced by role_1 at the higher threshold.")] IRole? role2 = null,
            [Summary("role_1_score", "Whole-number XP above 1000 and strictly greater than role_2_score.")] [MinValue(1001)] long? score1 = null,
            [Summary("role_2_score", "Whole-number XP above 1000 and strictly less than role_1_score.")] [MinValue(1001)] long? score2 = null)
        {
            RequireAdministrator();
            await SaveAsync(async db =>
            {
                var settings = await SettingsAsync(db);
                var first = role1?.Id ?? settings.Role1Id;
                var second = role2?.Id ?? settings.Role2Id;
                var firstScore = score1 ?? settings.Role1Score;
                var secondScore = score2 ?? settings.Role2Score;
                if (first == second || secondScore <= 1000 || firstScore <= secondScore)
                    throw new BotCommandException("Choose distinct roles and integer scores with role_1_score > role_2_score > 1000.");

                foreach (var id in new[] { first, second })
                {
                    var role = Context.Guild.GetRole(id) ?? throw new BotCommandException("Both activity roles must be configured in this server.");
                    DotNext.Optional<string> error = RoleReactionPolicy.ValidateRole(Context.Guild, role);
                    if (error.HasValue)
                        throw new BotCommandException(error.Value);
                }

                settings.Role1Id = first;
                settings.Role2Id = second;
                settings.Role1Score = firstScore;
                settings.Role2Score = secondScore;
            });
        }

        [SlashCommand("max_daily", "Set the maximum XP a member can earn in any rolling 24 hours.")]
        public async Task MaxDailyAsync([Summary("score", "Whole-number XP limit. Use 0 for unlimited earning.")] [MinValue(0)] long score)
        {
            RequireAdministrator();
            if (score < 0)
                throw new BotCommandException("The daily cap must be zero or a positive integer.");

            await SaveAsync(async db => (await SettingsAsync(db)).MaxDaily = score);
        }

        [SlashCommand("show", "Show activity roles, XP thresholds, the daily cap, and customised weights.")]
        public async Task ShowAsync([Summary("private", "Show the configuration only to you. Defaults to false.")] bool isPrivate = false)
        {
            RequireAdministrator();
            await DeferAsync(ephemeral: isPrivate);
            await RenderAsync(0, Context.User.Id);
        }

        [ComponentInteraction("activity-page:*:*:*", ignoreGroupNames: true)]
        public async Task PageAsync(ulong guildId, ulong ownerId, int page)
        {
            RequireAdministrator();
            if (guildId != Context.Guild.Id || ownerId != Context.User.Id
                || Context.Interaction is not SocketMessageComponent component || component.Message.Author.Id != Context.Client.CurrentUser.Id)
                throw new BotCommandException("Only the administrator who opened this configuration can turn its pages.");

            await component.DeferAsync();
            await RenderAsync(page, ownerId);
        }

        private async Task RenderAsync(int page, ulong ownerId)
        {
            await activity.RunAsync(Context.Guild.Id, async db =>
            {
                var settings = await db.ActivitySettings.FindAsync([Context.Guild.Id], lifetime.ApplicationStopping) ?? new ActivitySettings();
                Dictionary<ulong, decimal> weights = await db.ActivityWeights
                    .AsNoTracking()
                    .Where(row => row.GuildId == Context.Guild.Id)
                    .ToDictionaryAsync(row => row.ChannelId, row => row.Weight, lifetime.ApplicationStopping);
                var components = ActivityConfigurationView.Build(Context.Guild, settings, weights, page, ownerId);
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Flags = MessageFlags.ComponentsV2;
                    properties.Components = components;
                    properties.AllowedMentions = AllowedMentions.None;
                });
            }, lifetime.ApplicationStopping);
        }

        private async Task<ActivitySettings> SettingsAsync(BotDbContext db)
        {
            var settings = await db.ActivitySettings.FindAsync([Context.Guild.Id], lifetime.ApplicationStopping);
            if (settings is not null)
                return settings;

            settings = new ActivitySettings { GuildId = Context.Guild.Id };
            db.ActivitySettings.Add(settings);
            return settings;
        }

        private async Task SaveAsync(Func<BotDbContext, Task> change)
        {
            await DeferAsync(ephemeral: true);
            await activity.RunAsync(Context.Guild.Id, async db =>
            {
                await change(db);
                await db.SaveChangesAsync(lifetime.ApplicationStopping);
            }, lifetime.ApplicationStopping);
            await ModifyOriginalResponseAsync(properties => properties.Content = "Activity configuration saved. Changes apply on each member's next message.");
        }

        private void RequireAdministrator()
        {
            if (Context.User is not SocketGuildUser { GuildPermissions.Administrator: true })
                throw new BotCommandException("Only administrators can use /config.");
        }
    }
}
