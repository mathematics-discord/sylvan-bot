using SylvanBot.Interactions;

namespace SylvanBot.Features.SealHunts;

[RequireContext(ContextType.Guild)]
[UsedImplicitly]
public sealed class SealHuntModule(SealHuntWorker hunts, IHostApplicationLifetime lifetime) : SlashCommandBase
{
    [ComponentInteraction("seal:claim:*")]
    public Task ClaimAsync(string key) => RespondToSealAsync(key, claim: true);

    [ComponentInteraction("seal:abandon:*")]
    public Task AbandonAsync(string key) => RespondToSealAsync(key, claim: false);

    internal static MessageComponent BuildPost(SealHuntWorker.SealPhoto photo, string key)
    {
        MediaGalleryItemProperties attachment = new MediaGalleryItemProperties
        {
            Media = new UnfurledMediaItemProperties($"attachment://{photo.File}"), Description = photo.Credit,
        };

        ActionRowBuilder buttons = new ActionRowBuilder()
            .WithButton("Claim Him", $"seal:claim:{key}", ButtonStyle.Success)
            .WithButton("Abandon Him", $"seal:abandon:{key}", ButtonStyle.Danger);

        ContainerBuilder container = new ContainerBuilder()
            .WithMediaGallery(new MediaGalleryBuilder().AddItem(attachment))
            .WithTextDisplay($"-# {photo.Credit} · [Source]({photo.Source}) · [{photo.Licence}]({photo.LicenceUrl})")
            .WithActionRow(buttons);

        return new ComponentBuilderV2(container).Build();
    }

    private async Task RespondToSealAsync(string key, bool claim)
    {
        if (Context.Interaction is not SocketMessageComponent click || Context.User is not SocketGuildUser member)
            throw new BotCommandException("Use a button on the seal post.");

        Task<string> reply = hunts.ChooseAsync(click, member, key, claim, lifetime.ApplicationStopping);

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