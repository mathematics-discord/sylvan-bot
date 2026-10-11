namespace RatBot.Features.Logging.Gateway;

public sealed class BirbBackgroundWorker(DiscordSocketClient discordClient, ILogger logger) : BackgroundService
{
    private const ulong ChannelId = 268882317391429632;
    private const int MaxRetryCount = 5;
    private const MessageFlags Flags = MessageFlags.ComponentsV2;
    private static readonly AllowedMentions Mentions = new AllowedMentions(AllowedMentionTypes.None);
    private static readonly TimeSpan TtlSpan = TimeSpan.FromSeconds(30);
    private readonly string[] _imageUrls = LoadImageUrls();
    private readonly ILogger _logger = logger.ForContext<BirbBackgroundWorker>();
    private int _nextImageIndex;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WaitForReadyAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                DateTimeOffset nextHour = GetNextHour(DateTimeOffset.UtcNow);
                await DelayUntilAsync(nextHour, stoppingToken).ConfigureAwait(false);
                await PostHourlyAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Information("background worker is stopping");
        }
    }

    private static MessageComponent BuildComponents(string imageUrl)
    {
        MediaGalleryBuilder gallery = new MediaGalleryBuilder();
        gallery.AddItem(new MediaGalleryItemProperties { Media = new UnfurledMediaItemProperties(imageUrl) });

        return new ComponentBuilderV2(
            new ContainerBuilder()
                .WithMediaGallery(gallery)
                .WithActionRow(
                    new ActionRowBuilder().WithComponents([
                        new ButtonBuilder()
                            .WithStyle(ButtonStyle.Primary)
                            .WithLabel("Accept the proffered Joy?")
                            .WithCustomId(BirbResponseHandler.AcceptId),
                        new ButtonBuilder()
                            .WithStyle(ButtonStyle.Danger)
                            .WithLabel("Decline the Invitation")
                            .WithCustomId(BirbResponseHandler.DeclineId)
                    ])
                )
        ).Build();
    }

    private static async Task DelayUntilAsync(DateTimeOffset target, CancellationToken ct)
    {
        TimeSpan delay = target - DateTimeOffset.UtcNow;

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    private static DateTimeOffset GetNextHour(DateTimeOffset now)
    {
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
    }

    private static string[] LoadImageUrls()
    {
        return System.Text.Json.JsonSerializer.Deserialize<string[]>(
                   File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Features", "Logging", "Gateway",
                       "birb-images.json"))
               )
               ?? throw new InvalidOperationException("Robin image list is empty.");
    }

    public async Task<bool> PostOnceAsync(IMessageChannel channel, CancellationToken ct)
    {
        IUserMessage? message = null;

        try
        {
            message = await channel
                .SendMessageAsync(
                    options: new RequestOptions { CancelToken = ct },
                    allowedMentions: Mentions,
                    components: BuildComponents(_imageUrls[_nextImageIndex]),
                    flags: Flags
                )
                .ConfigureAwait(false);

            _nextImageIndex = (_nextImageIndex + 1) % _imageUrls.Length;
            _logger.Information("posted message {MessageId} to channel {ChannelId}", message.Id, ChannelId);

            await Task.Delay(TtlSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "post cycle failed");
        }

        if (message is null)
            return false;

        try
        {
            await message.DeleteAsync(new RequestOptions { CancelToken = ct }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "failed to delete {MessageId}", message.Id);
        }

        return true;
    }

    private async Task PostHourlyAsync(CancellationToken ct)
    {
        for (int retryCount = 0; retryCount <= MaxRetryCount; retryCount++)
        {
            if (discordClient.GetChannel(ChannelId) is not IMessageChannel channel)
            {
                _logger.Warning("channel {ChannelId} is unavailable", ChannelId);
                continue;
            }

            if (await PostOnceAsync(channel, ct).ConfigureAwait(false))
                return;
        }
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        if (discordClient.ConnectionState == ConnectionState.Connected)
            return;

        TaskCompletionSource ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        discordClient.Ready += OnReadyAsync;

        try
        {
            if (discordClient.ConnectionState == ConnectionState.Connected)
                return;

            await using CancellationTokenRegistration registration = ct.Register(() => ready.TrySetCanceled(ct));
            await ready.Task.ConfigureAwait(false);
        }
        finally
        {
            discordClient.Ready -= OnReadyAsync;
        }

        return;

        Task OnReadyAsync()
        {
            ready.TrySetResult();
            return Task.CompletedTask;
        }
    }
}