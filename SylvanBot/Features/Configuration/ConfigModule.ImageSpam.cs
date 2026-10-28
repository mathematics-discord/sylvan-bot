using SylvanBot.Features.Moderation;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [SlashCommand("image-spam", "View or update image spam detection settings.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ImageSpamConfigAsync(
        [Summary("number-of-channels", "Distinct channels required within the burst duration.")]
        int? numberOfChannels = null,
        [Summary("attachment-count", "Attachments required on a message before it counts toward detection.")]
        int? attachmentCount = null,
        [Summary("burst-duration", "Detection burst duration in seconds.")]
        int? burstDuration = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);

            return;
        }

        ErrorOr<Success> validationResult = Validate(
            numberOfChannels: numberOfChannels,
            attachmentCount: attachmentCount,
            burstDuration: burstDuration);

        if (validationResult.IsError)
        {
            await RespondAsync(text: validationResult.FirstError.Description, ephemeral: true);

            return;
        }

        await DeferAsync(ephemeral: true);
        var showCurrent = numberOfChannels is null && attachmentCount is null && burstDuration is null;
        Option<ImageBurstSpamDetectorOptions> settingsResult;

        if (showCurrent)
            settingsResult = await settingsService.GetCurrentAsync(Context.Guild.Id, lifetime.ApplicationStopping);
        else
        {
            var saved = await settingsService.UpsertAsync(
                Context.Guild.Id,
                numberOfChannels,
                attachmentCount,
                burstDuration,
                lifetime.ApplicationStopping);
            settingsResult = new Some<ImageBurstSpamDetectorOptions>(saved);
        }

        if (settingsResult.Value is not Some<ImageBurstSpamDetectorOptions>(var settings))
        {
            await ModifyOriginalResponseAsync(response => response.Content = "Image spam detection is not configured for this guild.");

            return;
        }

        await ModifyOriginalResponseAsync(response => response.Content = FormatSettings(settings));
    }

    private static string FormatSettings(ImageBurstSpamDetectorOptions settings) =>
        "Image spam detection settings: "
        + $"{settings.DistinctChannelThreshold} channel(s), "
        + $"{settings.RequiredAttachmentCount} attachment(s) per message, "
        + $"{settings.Window}s burst duration.";

    private static ErrorOr<Success> Validate(int? numberOfChannels, int? attachmentCount, int? burstDuration)
    {
        if (numberOfChannels is <= 0)
            return Error.Validation(description: "Number of channels must be greater than zero.");

        if (attachmentCount is <= 0)
            return Error.Validation(description: "Attachment count must be greater than zero.");

        return burstDuration is <= 0 ? Error.Validation(description: "Burst duration must be greater than zero seconds.") : Result.Success;
    }
}