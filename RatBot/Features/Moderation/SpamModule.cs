namespace RatBot.Features.Moderation;

using RatBot.Application.Common;
using RatBot.Interactions;

[Group("spam", "Spam moderation commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class SpamModule(ImageSpamSettingsService settingsService) : SlashCommandBase
{
    private static string FormatSettings(ImageBurstSpamDetectorOptions settings)
    {
        return "Image spam detection settings: "
               + $"{settings.DistinctChannelThreshold} channel(s), "
               + $"{settings.RequiredAttachmentCount} attachment(s) per message, "
               + $"{settings.Window}s burst duration.";
    }

    [SlashCommand("image-spam-config", "View or update image spam detection settings.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ImageSpamConfigAsync(
        [Summary("number-of-channels", "Distinct channels required within the burst duration.")]
        int? numberOfChannels = null,
        [Summary("attachment-count", "Attachments required on a message before it counts toward detection.")]
        int? attachmentCount = null,
        [Summary("burst-duration", "Detection burst duration in seconds.")]
        int? burstDuration = null
    )
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (numberOfChannels is <= 0)
        {
            await RespondAsync("Number of channels must be greater than zero.", ephemeral: true);
            return;
        }

        if (attachmentCount is <= 0)
        {
            await RespondAsync("Attachment count must be greater than zero.", ephemeral: true);
            return;
        }

        if (burstDuration is <= 0)
        {
            await RespondAsync("Burst duration must be greater than zero seconds.", ephemeral: true);
            return;
        }

        Option<ImageBurstSpamDetectorOptions> settingsResult;

        if (numberOfChannels is null && attachmentCount is null && burstDuration is null)
        {
            settingsResult = await settingsService.GetCurrentAsync(Context.Guild.Id, CancellationToken.None);
        }
        else
        {
            var updatedSettings = await settingsService
                .UpsertAsync(
                    Context.Guild.Id,
                    numberOfChannels,
                    attachmentCount,
                    burstDuration,
                    CancellationToken.None);

            settingsResult = new Some<ImageBurstSpamDetectorOptions>(updatedSettings);
        }

        if (settingsResult.Value is not Some<ImageBurstSpamDetectorOptions>(var settings))
        {
            await RespondAsync("Image spam detection is not configured for this guild.", ephemeral: true);
            return;
        }

        await RespondAsync(FormatSettings(settings), ephemeral: true);
    }
}