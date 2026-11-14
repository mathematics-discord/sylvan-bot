using SylvanBot.Interactions;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [SlashCommand("announcements", "Set the announcements channel and create its subscription role")]
    public async Task AnnouncementsAsync(
        [Summary("channel", "The channel for announcements and subscription buttons")]
        [ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel channel)
    {
        if (channel.GuildId != Context.Guild.Id)
            throw new BotCommandException("Choose a text or announcement channel from this server.");

        await DeferAsync(ephemeral: true);

        var roleId = await announcements.ConfigureAsync(Context.Guild, channel, lifetime.ApplicationStopping);

        await FollowupAsync(
            $"Announcements configured in <#{channel.Id}>. Subscribers receive <@&{roleId}>.\n"
            + "To offer this role in Channels & Roles, add it to an optional question in Server Settings > Onboarding.",
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }
}
