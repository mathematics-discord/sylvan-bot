using System.Collections.Concurrent;

namespace SylvanBot.Features.RoleReactions;

/// <summary>Serialises configuration and reaction effects for each guild.</summary>
public sealed class RoleReactionRuntime : IDisposable
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new ConcurrentDictionary<ulong, SemaphoreSlim>();

    public SemaphoreSlim ForGuild(ulong guildId) => _gates.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
            gate.Dispose();
    }
}