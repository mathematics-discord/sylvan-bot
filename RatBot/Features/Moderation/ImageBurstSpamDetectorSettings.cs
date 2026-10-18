namespace RatBot.Features.Moderation;

public sealed class ImageBurstSpamDetectorSettings(ImageBurstSpamDetectorOptions initialOptions)
{
    private readonly Dictionary<ulong, ImageBurstSpamDetectorOptions> _current = new();

    private readonly ImageBurstSpamDetectorOptions _defaultOptions = initialOptions;
    private readonly Lock _gate = new();

    public ImageBurstSpamDetectorSettings()
        : this(new ImageBurstSpamDetectorOptions())
    {
    }

    public ImageBurstSpamDetectorOptions Current
    {
        get
        {
            lock (_gate)
            {
                return _defaultOptions;
            }
        }
    }

    public bool TryGet(ulong guildId, out ImageBurstSpamDetectorOptions options)
    {
        lock (_gate)
        {
            return _current.TryGetValue(guildId, out options!);
        }
    }

    public void Update(ulong guildId, ImageBurstSpamDetectorOptions options)
    {
        lock (_gate)
        {
            _current[guildId] = options;
        }
    }

    public void Remove(ulong guildId)
    {
        lock (_gate)
        {
            _current.Remove(guildId);
        }
    }

    public void Update(ImageBurstSpamDetectorOptions options)
    {
        _ = options;
    }

    public void Update(int window, int distinctChannelThreshold)
    {
        _ = window;
        _ = distinctChannelThreshold;
    }

    public void Update(int window, int distinctChannelThreshold, int requiredAttachmentCount)
    {
        _ = window;
        _ = distinctChannelThreshold;
        _ = requiredAttachmentCount;
    }
}