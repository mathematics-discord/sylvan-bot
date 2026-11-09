namespace SylvanBot.Features.Tickets;

using System.Runtime.CompilerServices;

public sealed class TicketLocks(ILogger logger)
{
    private readonly Dictionary<(string Scope, ulong Id), Entry> _entries = [];

    public Task<IDisposable> TicketAsync(long id, CancellationToken token, [CallerMemberName] string operation = "")
        => AcquireAsync("ticket", checked((ulong)id), token, operation);

    public Task<IDisposable> ModeratorAsync(ulong id, CancellationToken token, [CallerMemberName] string operation = "")
        => AcquireAsync("moderator", id, token, operation);

    public Task<IDisposable> ConfigurationAsync(ulong id, CancellationToken token, [CallerMemberName] string operation = "")
        => AcquireAsync("configuration", id, token, operation);

    public async Task<IDisposable> TicketsAsync(IEnumerable<long> ids, CancellationToken token, [CallerMemberName] string operation = "")
    {
        var leases = new LeaseSet();

        try
        {
            IOrderedEnumerable<long> ordered = ids
                .Distinct()
                .Order();

            foreach (var id in ordered)
                leases.Items.Add(await TicketAsync(id, token, operation));

            return leases;
        }
        catch
        {
            leases.Dispose();
            throw;
        }
    }

    private async Task<IDisposable> AcquireAsync(string scope, ulong id, CancellationToken token, string operation)
    {
        var key = (scope, id);
        Entry entry;

        // This protects only the dictionary and reference counts, never ticket work.
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.Users++;
        }

        var timing = new OperationTiming(logger
            .ForContext("lock_scope", scope)
            .ForContext("lock_resource_id", id)
            .ForContext("ticket_operation", operation), "ticket.gate", "wait");

        try
        {
            await entry.Gate.WaitAsync(token);
        }
        catch
        {
            Release(key, entry, acquired: false);
            timing.Dispose();
            throw;
        }

        timing.Next("held");
        return new Lease(this, key, entry, timing);
    }

    private void Release((string Scope, ulong Id) key, Entry entry, bool acquired)
    {
        lock (_entries)
        {
            if (acquired)
                entry.Gate.Release();

            if (--entry.Users != 0)
                return;

            _entries.Remove(key);
            entry.Gate.Dispose();
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        public int Users;
    }

    private sealed class Lease(TicketLocks owner, (string Scope, ulong Id) key, Entry entry, OperationTiming timing) : IDisposable
    {
        private TicketLocks? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);

            if (current is null)
                return;

            timing.Dispose();
            current.Release(key, entry, acquired: true);
        }
    }

    private sealed class LeaseSet : IDisposable
    {
        public List<IDisposable> Items { get; } = [];

        public void Dispose()
        {
            for (var i = Items.Count - 1; i >= 0; i--)
                Items[i].Dispose();

            Items.Clear();
        }
    }
}
