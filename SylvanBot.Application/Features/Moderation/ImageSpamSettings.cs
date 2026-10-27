namespace SylvanBot.Features.Moderation;

public sealed class ImageSpamSettings
{
    private ImageSpamSettings()
    {
    }

    private ImageSpamSettings(
        ulong guildId,
        int requiredChannelCount,
        int requiredAttachmentCount,
        int burstDurationSeconds)
    {
        GuildId                 = guildId;
        RequiredChannelCount    = requiredChannelCount;
        RequiredAttachmentCount = requiredAttachmentCount;
        BurstDurationSeconds    = burstDurationSeconds;
        IsEnabled               = true;
    }

    public int BurstDurationSeconds { get; private set; }

    public ulong GuildId { get; private set; }

    public bool IsEnabled { get; private set; }

    public int RequiredAttachmentCount { get; private set; }

    public int RequiredChannelCount { get; private set; }

    public static ImageSpamSettings CreateDefault(ulong guildId) => new(guildId: guildId, requiredChannelCount: 4, requiredAttachmentCount: 2, burstDurationSeconds: 45);

    public void Disable()
    {
        IsEnabled = false;
    }

    public void Update(int? requiredChannelCount, int? requiredAttachmentCount, int? burstDurationSeconds)
    {
        RequiredChannelCount    = requiredChannelCount ?? RequiredChannelCount;
        RequiredAttachmentCount = requiredAttachmentCount ?? RequiredAttachmentCount;
        BurstDurationSeconds    = burstDurationSeconds ?? BurstDurationSeconds;
        IsEnabled               = true;
    }
}