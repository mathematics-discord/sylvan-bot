using RatBot.Interactions;

namespace RatBot.Features.Moderation;

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

    private static ErrorOr<Success> Validate(int? numberOfChannels, int? attachmentCount, int? burstDuration)
    {
        if (numberOfChannels is <= 0)
            return Error.Validation(description: "Number of channels must be greater than zero.");

        if (attachmentCount is <= 0)
            return Error.Validation(description: "Attachment count must be greater than zero.");

        return burstDuration is <= 0
            ? Error.Validation(description: "Burst duration must be greater than zero seconds.")
            : Result.Success;
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

        ErrorOr<Success> validationResult = Validate(numberOfChannels, attachmentCount, burstDuration);

        if (validationResult.IsError)
        {
            await RespondAsync(validationResult.FirstError.Description, ephemeral: true).ConfigureAwait(false);
            return;
        }

        Option<ImageBurstSpamDetectorOptions> settingsResult =
            numberOfChannels is null && attachmentCount is null && burstDuration is null
                ? await settingsService.GetCurrentAsync(Context.Guild.Id, CancellationToken.None)
                    .ConfigureAwait(false)
                : new Some<ImageBurstSpamDetectorOptions>(
                    await settingsService
                        .UpsertAsync(
                            Context.Guild.Id,
                            numberOfChannels,
                            attachmentCount,
                            burstDuration,
                            CancellationToken.None)
                        .ConfigureAwait(false));

        if (settingsResult.Value is not Some<ImageBurstSpamDetectorOptions>(var settings))
        {
            await RespondAsync("Image spam detection is not configured for this guild.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await RespondAsync(FormatSettings(settings), ephemeral: true).ConfigureAwait(false);
    }
}