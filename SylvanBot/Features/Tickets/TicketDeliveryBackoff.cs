namespace SylvanBot.Features.Tickets;

// Shared delivery state. Moderation operations do not wait on these cooldowns.
public sealed class TicketDeliveryBackoff(TimeProvider clock)
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<ulong, (DateTimeOffset Until, int Failures)> _channels = [];
    private readonly Dictionary<ulong, (DateTimeOffset Until, int Failures)> _moderators = [];
    private DateTimeOffset _globalUntil;

    internal bool IsDeferred(ulong channelId) => Remaining(channelId) > TimeSpan.Zero;
    internal bool IsModeratorDeferred(ulong userId) => Remaining(_moderators, userId) > TimeSpan.Zero;
    internal TimeSpan Remaining(ulong channelId) => Remaining(_channels, channelId);
    internal void Failed(ulong channelId) => Failed(_channels, channelId);
    internal void ModeratorFailed(ulong userId) => Failed(_moderators, userId);
    internal void Succeeded(ulong channelId) => Succeeded(_channels, channelId);
    internal void ModeratorSucceeded(ulong userId) => Succeeded(_moderators, userId);

    internal void DeferGlobal(DateTimeOffset until)
    {
        lock (_gate)
        {
            if (until > _globalUntil)
                _globalUntil = until;
        }
    }

    internal void DeferChannel(ulong channelId, DateTimeOffset until) => Defer(_channels, channelId, until);

    private TimeSpan Remaining(Dictionary<ulong, (DateTimeOffset Until, int Failures)> entries, ulong id)
    {
        lock (_gate)
        {
            var until = entries.GetValueOrDefault(id).Until;
            var remaining = (until > _globalUntil ? until : _globalUntil) - clock.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    private void Failed(Dictionary<ulong, (DateTimeOffset Until, int Failures)> entries, ulong id)
    {
        lock (_gate)
        {
            var previous = entries.GetValueOrDefault(id);
            var seconds = Math.Min(300, 30 * (1 << previous.Failures));
            var until = clock.GetUtcNow().AddSeconds(seconds);
            entries[id] = (until > previous.Until ? until : previous.Until, Math.Min(4, previous.Failures + 1));
        }
    }

    private void Succeeded(Dictionary<ulong, (DateTimeOffset Until, int Failures)> entries, ulong id)
    {
        lock (_gate)
        {
            if (entries.TryGetValue(id, out var previous))
                entries[id] = (previous.Until, 0);
        }
    }

    private void Defer(Dictionary<ulong, (DateTimeOffset Until, int Failures)> entries, ulong id, DateTimeOffset until)
    {
        lock (_gate)
        {
            var previous = entries.GetValueOrDefault(id);
            entries[id] = (until > previous.Until ? until : previous.Until, previous.Failures);
        }
    }

    internal RequestOptions Options(ulong channelId, CancellationToken token) => Options(until => DeferChannel(channelId, until), token);
    internal RequestOptions ModeratorOptions(ulong userId, CancellationToken token) => Options(until => Defer(_moderators, userId, until), token);

    private RequestOptions Options(Action<DateTimeOffset> defer, CancellationToken token)
    {
        var options = TicketBoundary.Options(token);
        options.RatelimitCallback = info =>
        {
            if (info.Remaining != 0 && !info.IsGlobal)
                return Task.CompletedTask;

            var until = info.Reset ?? clock.GetUtcNow().Add(info.ResetAfter ?? TimeSpan.FromSeconds(info.RetryAfter ?? 30));

            if (info.IsGlobal)
                DeferGlobal(until);
            else
                defer(until);

            return Task.CompletedTask;
        };

        return options;
    }
}
