using SylvanBot.Features.SealHunts;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [Group("seal", "Manage additional seal hunt destinations")]
    [CommandContextType(InteractionContextType.Guild)]
    [RequireContext(ContextType.Guild)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public sealed class SealConfigModule(SealHuntWorker hunts, IHostApplicationLifetime lifetime) : SlashCommandBase
    {
        [SlashCommand("allow", "Allow a channel, category, or thread for seal hunts")]
        public Task AllowAsync(IChannel destination) => SetAsync(destination, allow: true);

        [SlashCommand("remove", "Restore a seal hunt destination's default eligibility")]
        public Task RemoveAsync(IChannel destination) => SetAsync(destination, allow: false);

        [SlashCommand("list", "Show this server's extra seal hunt destinations")]
        public async Task ListAsync()
        {
            await DeferAsync(ephemeral: true);
            HashSet<long> allowed = await hunts.GetDestinationsAsync(Context.Guild.Id, lifetime.ApplicationStopping);

            if (allowed.Count == 0)
            {
                await FollowupAsync("There are no additional seal hunt destinations.", ephemeral: true);
                return;
            }

            IEnumerable<string[]> pages = allowed
                .Order()
                .Select(id => $"<#{id}>")
                .Chunk(75);

            foreach (var page in pages)
                await FollowupAsync(string.Join(", ", page), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private async Task SetAsync(IChannel destination, bool allow)
        {
            if (destination is not IGuildChannel target || target.GuildId != Context.Guild.Id)
                throw new BotCommandException("Choose a destination in this server.");

            var supported = target is IMessageChannel or ICategoryChannel or IForumChannel or IMediaChannel;

            if (!supported)
                throw new BotCommandException("Choose a message channel, category, or thread.");

            await DeferAsync(ephemeral: true);
            await hunts.SetDestinationAsync(Context.Guild.Id, target.Id, allow, lifetime.ApplicationStopping);

            var text = allow
                ? $"Added <#{target.Id}> to the seal hunt allow list."
                : $"Removed the explicit allow-list entry for <#{target.Id}>.";

            await FollowupAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
    }
}