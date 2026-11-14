using SylvanBot.Interactions;

namespace SylvanBot.Features.Announcements;

[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
public sealed class AnnouncementModule(AnnouncementRuntime announcements, IHostApplicationLifetime lifetime) : SlashCommandBase
{
    [SlashCommand("announce", "Write and send an announcement to subscribers")]
    [DefaultMemberPermissions(GuildPermission.ViewAuditLog)]
    [RequireUserPermission(GuildPermission.ViewAuditLog)]
    public Task AnnounceAsync() => RespondWithModalAsync<AnnouncementModal>("announcements:send");

    [ModalInteraction("announcements:send")]
    [RequireUserPermission(GuildPermission.ViewAuditLog)]
    public async Task SendAsync(AnnouncementModal modal)
    {
        await DeferAsync(ephemeral: true);

        var result = await announcements.PublishAsync(Context.Guild, modal.AnnouncementTitle, modal.Body, lifetime.ApplicationStopping);

        await FollowupAsync(result, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction(AnnouncementRuntime.SubscribeId)]
    public Task SubscribeAsync() => SetSubscriptionAsync(subscribe: true);

    [ComponentInteraction(AnnouncementRuntime.UnsubscribeId)]
    public Task UnsubscribeAsync() => SetSubscriptionAsync(subscribe: false);

    private async Task SetSubscriptionAsync(bool subscribe)
    {
        await DeferAsync(ephemeral: true);

        if (Context.Interaction is not SocketMessageComponent component)
            throw new BotCommandException("Use the subscription buttons in the announcements channel.");

        await announcements.SetSubscriptionAsync(component, subscribe, lifetime.ApplicationStopping);
        await FollowupAsync(subscribe ? "You are subscribed to announcements." : "You are unsubscribed from announcements.", ephemeral: true);
    }
}
