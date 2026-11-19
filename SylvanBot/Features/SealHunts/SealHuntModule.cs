using SylvanBot.Features.SealHunts;
using SylvanBot.Interactions;

namespace SylvanBot.Features.SealHunts
{
    [RequireContext(ContextType.Guild)]
    public sealed class SealHuntModule(SealHuntWorker hunts, IHostApplicationLifetime lifetime) : SlashCommandBase
    {
        internal static MessageComponent BuildPost(SealHuntWorker.SealPhoto photo, string key)
        {
            var attachment = new MediaGalleryItemProperties
            {
                Media = new UnfurledMediaItemProperties($"attachment://{photo.File}"),
                Description = photo.Credit,
            };
            var buttons = new ActionRowBuilder()
                .WithButton("Claim Him", $"seal:claim:{key}", ButtonStyle.Success)
                .WithButton("Abandon Him", $"seal:abandon:{key}", ButtonStyle.Danger);

            var container = new ContainerBuilder()
                .WithMediaGallery(new MediaGalleryBuilder().AddItem(attachment))
                .WithTextDisplay($"-# {photo.Credit} · [Source]({photo.Source}) · [{photo.Licence}]({photo.LicenceUrl})")
                .WithActionRow(buttons);

            return new ComponentBuilderV2(container).Build();
        }

        internal static bool ContainsClaim(IMessage message, string claimId) =>
            message.Components
                .OfType<ContainerComponent>()
                .SelectMany(container => container.Components)
                .OfType<ActionRowComponent>()
                .SelectMany(row => row.Components)
                .OfType<ButtonComponent>()
                .Any(button => string.Equals(button.CustomId, claimId, StringComparison.Ordinal));

        [ComponentInteraction("seal:claim:*")]
        public Task ClaimAsync(string key) => RespondToSealAsync(key, claim: true);

        [ComponentInteraction("seal:abandon:*")]
        public Task AbandonAsync(string key) => RespondToSealAsync(key, claim: false);

        private async Task RespondToSealAsync(string key, bool claim)
        {
            if (Context.Interaction is not SocketMessageComponent click || Context.User is not SocketGuildUser member)
                throw new BotCommandException("Use a button on the seal post.");

            var reply = hunts.ChooseAsync(click, member, key, claim, lifetime.ApplicationStopping);
            if (reply.IsCompletedSuccessfully)
            {
                await RespondAsync(await reply, ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            await Task.WhenAll(reply, click.DeferLoadingAsync(ephemeral: true));
            var confirmation = await reply;
            await ModifyOriginalResponseAsync(message => message.Content = confirmation);
        }
    }
}

namespace SylvanBot.Features.Configuration
{
    public sealed partial class ConfigModule
    {
        [Group("seal", "Manage additional seal hunt destinations")]
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
                var allowed = await hunts.GetDestinationsAsync(Context.Guild.Id, lifetime.ApplicationStopping);
                if (allowed.Count == 0)
                {
                    await FollowupAsync("There are no additional seal hunt destinations.", ephemeral: true);
                    return;
                }

                var pages = allowed
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
}
